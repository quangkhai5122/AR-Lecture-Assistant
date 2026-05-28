from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any

import requests

from services.errors import PipelineError


@dataclass
class GeminiTextResult:
    text: str
    provider: str
    model: str
    mock_used: bool
    warnings: list[str]


class GeminiService:
    """Gemini adapter for lecture-aware translation and summarization."""

    SUPPORTED_PROVIDERS = {"mock", "gemini"}

    def translate_sentence(
        self,
        text: str,
        source_language: str = "en-US",
        target_language: str = "vi",
        context: list[str] | None = None,
        force_mock: bool = False,
        provider: str | None = None,
    ) -> GeminiTextResult:
        resolved_provider = self._resolve_provider(force_mock, provider)
        normalized_text = " ".join((text or "").split())
        if not normalized_text:
            return GeminiTextResult(
                text="",
                provider=resolved_provider,
                model=self._model(),
                mock_used=resolved_provider == "mock",
                warnings=["No text to translate."],
            )

        if resolved_provider == "mock":
            return GeminiTextResult(
                text=f"[VI-MOCK] {normalized_text}",
                provider="mock",
                model="mock",
                mock_used=True,
                warnings=[],
            )

        prompt = self._build_translation_prompt(
            text=normalized_text,
            source_language=source_language,
            target_language=target_language,
            context=context or [],
        )
        return self._generate_text(prompt)

    def summarize_notes(
        self,
        text: str,
        target_language: str = "vi",
        force_mock: bool = False,
        provider: str | None = None,
    ) -> GeminiTextResult:
        resolved_provider = self._resolve_provider(force_mock, provider)
        normalized_text = " ".join((text or "").split())
        if not normalized_text:
            return GeminiTextResult(
                text="",
                provider=resolved_provider,
                model=self._model(),
                mock_used=resolved_provider == "mock",
                warnings=["No text to summarize."],
            )

        if resolved_provider == "mock":
            return GeminiTextResult(
                text=f"[SUMMARY-MOCK] {normalized_text[:240]}",
                provider="mock",
                model="mock",
                mock_used=True,
                warnings=[],
            )

        prompt = (
            "Summarize the following lecture notes in concise Vietnamese. "
            "Keep technical terms accurate and preserve formulas.\n\n"
            f"Target language: {target_language}\n\n"
            f"Lecture notes:\n{normalized_text}"
        )
        return self._generate_text(prompt)

    def _resolve_provider(self, force_mock: bool, provider: str | None) -> str:
        if force_mock:
            return "mock"

        selected = (provider or os.getenv("LLM_PROVIDER") or "gemini").strip().lower()
        if selected not in self.SUPPORTED_PROVIDERS:
            raise PipelineError(
                "Unsupported LLM provider "
                f"'{selected}'. Expected one of: {sorted(self.SUPPORTED_PROVIDERS)}."
            )
        return selected

    def _build_translation_prompt(
        self,
        text: str,
        source_language: str,
        target_language: str,
        context: list[str],
    ) -> str:
        context_lines = "\n".join(f"- {item}" for item in context[-5:] if item and item.strip())
        return (
            "You are translating a university lecture transcript. "
            "Translate only the current sentence into Vietnamese, using the context only to disambiguate terms. "
            "Return only the translated sentence, with no explanation.\n\n"
            f"Source language: {source_language}\n"
            f"Target language: {target_language}\n\n"
            f"Recent context:\n{context_lines or '-'}\n\n"
            f"Current sentence:\n{text}"
        )

    def _generate_text(self, prompt: str) -> GeminiTextResult:
        api_key = os.getenv("GEMINI_API_KEY") or os.getenv("GOOGLE_API_KEY")
        if not api_key:
            raise PipelineError(
                "LLM_PROVIDER=gemini requires GEMINI_API_KEY or GOOGLE_API_KEY.",
                code="llm_provider_not_configured",
            )

        model = self._model()
        base_url = os.getenv("GEMINI_API_BASE_URL", "https://generativelanguage.googleapis.com/v1beta")
        url = f"{base_url.rstrip('/')}/models/{model}:generateContent"
        payload = {
            "contents": [
                {
                    "role": "user",
                    "parts": [{"text": prompt}],
                }
            ],
            "generationConfig": {
                "temperature": float(os.getenv("GEMINI_TEMPERATURE", "0.2")),
            },
        }

        timeout = float(os.getenv("GEMINI_TIMEOUT_SECONDS", "30"))
        try:
            response = requests.post(url, params={"key": api_key}, json=payload, timeout=timeout)
            response.raise_for_status()
            data = response.json()
        except Exception as exc:
            raise PipelineError(
                f"Gemini request failed: {exc}",
                status_code=502,
                code="llm_provider_failed",
            ) from exc

        text = self._parse_generate_content_response(data)
        return GeminiTextResult(
            text=text,
            provider="gemini",
            model=model,
            mock_used=False,
            warnings=[] if text else ["Gemini returned an empty response."],
        )

    def _parse_generate_content_response(self, data: dict[str, Any]) -> str:
        if not isinstance(data, dict):
            raise PipelineError(
                f"Unexpected Gemini response shape: {data}",
                status_code=502,
                code="llm_provider_bad_response",
            )

        candidates = data.get("candidates")
        if not isinstance(candidates, list) or not candidates:
            raise PipelineError(
                f"Unexpected Gemini response shape: {data}",
                status_code=502,
                code="llm_provider_bad_response",
            )

        content = candidates[0].get("content") if isinstance(candidates[0], dict) else None
        parts = content.get("parts") if isinstance(content, dict) else None
        if not isinstance(parts, list):
            raise PipelineError(
                f"Unexpected Gemini response shape: {data}",
                status_code=502,
                code="llm_provider_bad_response",
            )

        text_parts = [str(part.get("text", "")) for part in parts if isinstance(part, dict)]
        return "\n".join(part.strip() for part in text_parts if part.strip()).strip()

    def _model(self) -> str:
        return os.getenv("GEMINI_MODEL", "gemini-2.5-flash-lite").strip()
