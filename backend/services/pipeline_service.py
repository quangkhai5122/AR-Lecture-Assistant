from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any

from services.formula_service import FormulaService
from services.ocr_service import OCRResult, OCRService
from services.translation_service import TranslationResult, TranslationService


@dataclass
class PreparedBlock:
    block: dict[str, Any]
    block_type: str
    masked_text: str
    formula_mapping: dict[str, str]
    translation_index: int | None = None


class PipelineService:
    """Orchestrates OCR -> formula masking -> batch translation -> restore."""

    def __init__(self):
        self.ocr_service = OCRService()
        self.formula_service = FormulaService()
        self.translation_service = TranslationService()

    def process_frame(self, payload: dict[str, Any]) -> dict[str, Any]:
        start = time.perf_counter()
        frame_id = payload.get("frame_id") or "frame_unknown"
        target_language = payload.get("target_language") or "vi"
        force_mock = bool(payload.get("mock", True))

        ocr_start = time.perf_counter()
        ocr_result = self.ocr_service.recognize(
            image_base64=payload.get("image_base64") or "",
            image_width=payload.get("image_width"),
            image_height=payload.get("image_height"),
            force_mock=force_mock,
            provider=payload.get("ocr_provider"),
        )
        ocr_ms = self._elapsed_ms(ocr_start)

        translation_start = time.perf_counter()
        translated_blocks, translation_result = self.translate_blocks_preserving_formula(
            ocr_result.blocks,
            target_language=target_language,
            force_mock=force_mock,
            provider=payload.get("translation_provider"),
        )
        translation_ms = self._elapsed_ms(translation_start)

        warnings = [*ocr_result.warnings, *translation_result.warnings]
        if translation_result.cache_hits:
            warnings.append(f"translation cache hits: {translation_result.cache_hits}")

        return {
            "frame_id": frame_id,
            "image_width": ocr_result.image_width,
            "image_height": ocr_result.image_height,
            "blocks": translated_blocks,
            "provider": {
                "ocr": ocr_result.provider,
                "translation": translation_result.provider,
            },
            "mock_used": ocr_result.mock_used or translation_result.mock_used,
            "warnings": warnings,
            "latency_ms": {
                "ocr": ocr_ms,
                "translation": translation_ms,
                "total": self._elapsed_ms(start),
            },
        }

    def translate_blocks_preserving_formula(
        self,
        blocks: list[dict[str, Any]],
        target_language: str,
        force_mock: bool = True,
        provider: str | None = None,
    ) -> tuple[list[dict[str, Any]], TranslationResult]:
        prepared_blocks: list[PreparedBlock] = []
        translation_inputs: list[str] = []

        for block in blocks:
            source_text = str(block.get("text", ""))
            masked = self.formula_service.mask(source_text)
            prepared = PreparedBlock(
                block=block,
                block_type=masked.block_type,
                masked_text=masked.text,
                formula_mapping=masked.mapping,
            )

            if masked.block_type != "formula" and masked.text.strip():
                prepared.translation_index = len(translation_inputs)
                translation_inputs.append(masked.text)

            prepared_blocks.append(prepared)

        translation_result = self.translation_service.translate_batch(
            translation_inputs,
            target_language=target_language,
            force_mock=force_mock,
            provider=provider,
        )

        translated_blocks: list[dict[str, Any]] = []
        for prepared in prepared_blocks:
            source_text = str(prepared.block.get("text", ""))
            if prepared.block_type == "formula":
                translated_text = source_text
            elif prepared.translation_index is None:
                translated_text = source_text
            else:
                translated = translation_result.translations[prepared.translation_index]
                translated_text = self.formula_service.restore(translated, prepared.formula_mapping)

            translated_blocks.append({
                "id": prepared.block.get("id", ""),
                "source_text": source_text,
                "translated_text": translated_text,
                "bbox": prepared.block.get("bbox", [0, 0, 0, 0]),
                "confidence": float(prepared.block.get("confidence", 0.0)),
                "type": prepared.block_type,
                "style": {
                    "font_size": 34 if prepared.block_type == "formula" else 38,
                    "background_alpha": 0.55 if prepared.block_type == "formula" else 0.68,
                },
            })

        return translated_blocks, translation_result

    def translate_preserving_formula(
        self,
        text: str,
        target_language: str,
        force_mock: bool = True,
        provider: str | None = None,
    ) -> tuple[str, str]:
        blocks, _ = self.translate_blocks_preserving_formula(
            [{"id": "text", "text": text, "bbox": [0, 0, 0, 0], "confidence": 1.0}],
            target_language=target_language,
            force_mock=force_mock,
            provider=provider,
        )
        block = blocks[0]
        return block["translated_text"], block["type"]

    def _elapsed_ms(self, start: float) -> float:
        return round((time.perf_counter() - start) * 1000, 2)
