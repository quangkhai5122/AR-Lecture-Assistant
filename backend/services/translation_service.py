from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any

import requests

from services.errors import PipelineError


@dataclass
class TranslationResult:
    translations: list[str]
    provider: str
    mock_used: bool
    warnings: list[str]
    cache_hits: int = 0
    cache_misses: int = 0


class TranslationService:
    """Translation provider adapter with batch calls and in-memory caching."""

    SUPPORTED_PROVIDERS = {"mock", "libretranslate"}

    GLOSSARY = {
        "machine learning": "học máy",
        "deep learning": "học sâu",
        "neural networks": "mạng nơ-ron",
        "neural network": "mạng nơ-ron",
        "loss": "hàm mất mát",
        "loss function": "hàm mất mát",
        "gradient descent": "hạ dốc gradient",
        "model weights": "trọng số mô hình",
        "weights": "trọng số",
        "artificial intelligence": "trí tuệ nhân tạo",
    }

    MOCK_SENTENCES = {
        "Deep learning uses neural networks.": "Học sâu sử dụng mạng nơ-ron.",
        "Gradient descent updates the model weights.": "Hạ dốc gradient cập nhật trọng số mô hình.",
        "The loss is [FORMULA_0].": "Hàm mất mát là [FORMULA_0].",
        "The loss is L = -Σ y log(p).": "Hàm mất mát là L = -Σ y log(p).",
        "Machine learning is a field of artificial intelligence.": "Học máy là một lĩnh vực của trí tuệ nhân tạo.",
    }

    def __init__(self):
        self._cache: dict[tuple[str, str, str], str] = {}

    def translate_batch(
        self,
        texts: list[str],
        target_language: str = "vi",
        force_mock: bool = True,
        provider: str | None = None,
    ) -> TranslationResult:
        resolved_provider = self._resolve_provider(force_mock, provider)
        if not texts:
            return TranslationResult(
                translations=[],
                provider=resolved_provider,
                mock_used=resolved_provider == "mock",
                warnings=[],
            )

        if resolved_provider == "mock":
            return TranslationResult(
                translations=[self._mock_translate(text) for text in texts],
                provider="mock",
                mock_used=True,
                warnings=[],
            )

        if resolved_provider != "libretranslate":
            raise PipelineError(f"Unsupported translation provider: {resolved_provider}")

        libre_url = os.getenv("LIBRETRANSLATE_URL")
        if not libre_url:
            raise PipelineError(
                "TRANSLATION_PROVIDER=libretranslate requires LIBRETRANSLATE_URL.",
                code="translation_provider_not_configured",
            )

        results: list[str | None] = [None] * len(texts)
        missing_texts: list[str] = []
        missing_keys: list[tuple[str, str, str]] = []
        missing_indexes: list[int] = []
        cache_hits = 0

        for index, text in enumerate(texts):
            key = self._cache_key(resolved_provider, target_language, text)
            if key in self._cache:
                results[index] = self._cache[key]
                cache_hits += 1
            else:
                missing_texts.append(text)
                missing_keys.append(key)
                missing_indexes.append(index)

        if missing_texts:
            translated = self._translate_libre_batch(missing_texts, target_language, libre_url)
            for key, index, translated_text in zip(missing_keys, missing_indexes, translated):
                self._cache[key] = translated_text
                results[index] = translated_text

        return TranslationResult(
            translations=[text if text is not None else "" for text in results],
            provider=resolved_provider,
            mock_used=False,
            warnings=[],
            cache_hits=cache_hits,
            cache_misses=len(missing_texts),
        )

    def translate(
        self,
        text: str,
        target_language: str = "vi",
        force_mock: bool = True,
        provider: str | None = None,
    ) -> str:
        return self.translate_batch([text], target_language, force_mock, provider).translations[0]

    def _resolve_provider(self, force_mock: bool, provider: str | None) -> str:
        if force_mock:
            return "mock"

        selected = (provider or os.getenv("TRANSLATION_PROVIDER") or "libretranslate").strip().lower()
        if selected not in self.SUPPORTED_PROVIDERS:
            raise PipelineError(
                "Unsupported translation provider "
                f"'{selected}'. Expected one of: {sorted(self.SUPPORTED_PROVIDERS)}."
            )
        return selected

    def _translate_libre_batch(self, texts: list[str], target_language: str, url: str) -> list[str]:
        payload: dict[str, Any] = {
            "q": texts if len(texts) > 1 else texts[0],
            "source": "auto",
            "target": target_language,
            "format": "text",
        }
        api_key = os.getenv("LIBRETRANSLATE_API_KEY")
        if api_key:
            payload["api_key"] = api_key

        timeout = float(os.getenv("LIBRETRANSLATE_TIMEOUT_SECONDS", "15"))
        try:
            response = requests.post(url, json=payload, timeout=timeout)
            response.raise_for_status()
            data = response.json()
        except Exception as exc:
            raise PipelineError(
                f"LibreTranslate request failed: {exc}",
                status_code=502,
                code="translation_provider_failed",
            ) from exc

        translated = data.get("translatedText")
        if isinstance(translated, str):
            if len(texts) == 1:
                return [translated]
            raise PipelineError(
                "LibreTranslate returned a single translation for a batch request.",
                status_code=502,
                code="translation_provider_bad_response",
            )
        if isinstance(translated, list) and len(translated) == len(texts):
            return [str(item) for item in translated]

        raise PipelineError(
            f"Unexpected LibreTranslate response shape: {data}",
            status_code=502,
            code="translation_provider_bad_response",
        )

    def _cache_key(self, provider: str, target_language: str, text: str) -> tuple[str, str, str]:
        return (provider, target_language, " ".join(text.split()))

    def _mock_translate(self, text: str) -> str:
        if text in self.MOCK_SENTENCES:
            return self.MOCK_SENTENCES[text]

        lowered = text.lower()
        translated = text

        for src, vi in sorted(self.GLOSSARY.items(), key=lambda kv: len(kv[0]), reverse=True):
            if src in lowered:
                translated = translated.replace(src, vi)
                translated = translated.replace(src.title(), vi.capitalize())
                translated = translated.replace(src.capitalize(), vi.capitalize())

        if translated != text:
            return translated
        return f"[VI-MOCK] {text}"
