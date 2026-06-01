from __future__ import annotations

import os
from typing import Any

import requests


class TranslationService:
    """Translation layer.

    MVP có mock translation để demo không phụ thuộc API/network.

    TODO(MVP) Translation:
    - Chọn dịch thật: ML Kit on-device, Google Cloud Translation, OpenAI, NLLB, MarianMT, LibreTranslate.
    - Thêm glossary thuật ngữ AI/ML theo môn học.
    - Thêm batch translate để giảm latency.
    - Thêm cache theo source_text để không dịch lại liên tục.
    - Đánh dấu language detection nếu slide không phải tiếng Anh.
    """

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

    def translate(self, text: str, target_language: str = "vi") -> str:
        if not text.strip():
            return text

        libre_url = os.getenv("LIBRETRANSLATE_URL")
        if libre_url:
            try:
                return self._translate_libre(text, target_language, libre_url)
            except Exception as exc:
                print(f"[TranslationService] LibreTranslate failed, fallback mock: {exc}")

        return self._mock_translate(text)

    def _translate_libre(self, text: str, target_language: str, url: str) -> str:
        payload: dict[str, Any] = {
            "q": text,
            "source": "auto",
            "target": target_language,
            "format": "text",
        }
        api_key = os.getenv("LIBRETRANSLATE_API_KEY")
        if api_key:
            payload["api_key"] = api_key

        response = requests.post(url, json=payload, timeout=15)
        response.raise_for_status()
        data = response.json()
        return data.get("translatedText", text)

    def _mock_translate(self, text: str) -> str:
        if text in self.MOCK_SENTENCES:
            return self.MOCK_SENTENCES[text]

        lowered = text.lower()
        translated = text

        # Heuristic cực đơn giản để demo glossary. Không dùng cho production.
        for src, vi in sorted(self.GLOSSARY.items(), key=lambda kv: len(kv[0]), reverse=True):
            if src in lowered:
                translated = translated.replace(src, vi)
                translated = translated.replace(src.title(), vi.capitalize())
                translated = translated.replace(src.capitalize(), vi.capitalize())

        if translated != text:
            return translated

        # Fallback rõ ràng để người demo biết đây chưa phải dịch thật.
        return f"[VI-MOCK] {text}"
