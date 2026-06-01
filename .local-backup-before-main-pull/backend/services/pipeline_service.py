from __future__ import annotations

from typing import Any

from services.formula_service import FormulaService
from services.ocr_service import OCRService
from services.translation_service import TranslationService


class PipelineService:
    """Orchestrates OCR -> formula masking -> translation -> restore.

    Đây là contract trung tâm giúp Unity không cần biết backend dùng OCR/dịch gì.
    """

    def __init__(self):
        self.ocr_service = OCRService()
        self.formula_service = FormulaService()
        self.translation_service = TranslationService()

    def process_frame(self, payload: dict[str, Any]) -> dict[str, Any]:
        frame_id = payload.get("frame_id") or "frame_unknown"
        image_base64 = payload.get("image_base64") or ""
        target_language = payload.get("target_language") or "vi"
        force_mock = bool(payload.get("mock", True))

        image_width = payload.get("image_width")
        image_height = payload.get("image_height")

        blocks = self.ocr_service.recognize(
            image_base64=image_base64,
            image_width=image_width,
            image_height=image_height,
            force_mock=force_mock,
        )

        # Nếu OCR thật trả width/height chưa có, lấy fallback theo mock/default.
        # TODO(MVP): Backend nên trả đúng kích thước ảnh sau khi decode.
        response_width = int(image_width or 1280)
        response_height = int(image_height or 720)

        translated_blocks: list[dict[str, Any]] = []
        for block in blocks:
            source_text = block.get("text", "")
            translated_text, block_type = self.translate_preserving_formula(source_text, target_language)

            translated_blocks.append({
                "id": block.get("id", ""),
                "source_text": source_text,
                "translated_text": translated_text,
                "bbox": block.get("bbox", [0, 0, 0, 0]),
                "confidence": float(block.get("confidence", 0.0)),
                "type": block_type,
                "style": {
                    "font_size": 34 if block_type == "formula" else 38,
                    "background_alpha": 0.55 if block_type == "formula" else 0.68,
                },
            })

        return {
            "frame_id": frame_id,
            "image_width": response_width,
            "image_height": response_height,
            "blocks": translated_blocks,
        }

    def translate_preserving_formula(self, text: str, target_language: str) -> tuple[str, str]:
        masked = self.formula_service.mask(text)
        if masked.block_type == "formula":
            return text, "formula"

        translated = self.translation_service.translate(masked.text, target_language)
        restored = self.formula_service.restore(translated, masked.mapping)
        return restored, masked.block_type
