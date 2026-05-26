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
            "document_surface": self.estimate_document_surface(
                ocr_result.blocks,
                ocr_result.image_width,
                ocr_result.image_height,
            ),
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

    def estimate_document_surface(
        self,
        blocks: list[dict[str, Any]],
        image_width: int,
        image_height: int,
    ) -> dict[str, Any] | None:
        """Estimate slide/board corners from OCR boxes.

        The mobile client can project these corners onto the AR plane and place
        labels by surface coordinates instead of raycasting every text center.
        """

        valid_boxes: list[list[float]] = []
        for block in blocks:
            bbox = block.get("bbox")
            if not isinstance(bbox, list) or len(bbox) < 4:
                continue

            try:
                x1, y1, x2, y2 = [float(value) for value in bbox[:4]]
            except (TypeError, ValueError):
                continue

            if x2 <= x1 or y2 <= y1:
                continue

            valid_boxes.append([x1, y1, x2, y2])

        if not valid_boxes or image_width <= 0 or image_height <= 0:
            return None

        x1 = min(box[0] for box in valid_boxes)
        y1 = min(box[1] for box in valid_boxes)
        x2 = max(box[2] for box in valid_boxes)
        y2 = max(box[3] for box in valid_boxes)

        content_width = max(1.0, x2 - x1)
        content_height = max(1.0, y2 - y1)
        pad_x = max(image_width * 0.04, content_width * 0.10)
        pad_y = max(image_height * 0.05, content_height * 0.35)

        x1 = self._clamp_float(x1 - pad_x, 0.0, float(image_width))
        y1 = self._clamp_float(y1 - pad_y, 0.0, float(image_height))
        x2 = self._clamp_float(x2 + pad_x, 0.0, float(image_width))
        y2 = self._clamp_float(y2 + pad_y, 0.0, float(image_height))

        if x2 <= x1 or y2 <= y1:
            return None

        coverage = ((x2 - x1) * (y2 - y1)) / max(1.0, float(image_width * image_height))
        confidence = max(0.25, min(0.85, 0.35 + coverage))

        return {
            "corners": [
                round(x1, 2), round(y1, 2),
                round(x2, 2), round(y1, 2),
                round(x2, 2), round(y2, 2),
                round(x1, 2), round(y2, 2),
            ],
            "confidence": round(confidence, 3),
            "method": "ocr_bbox_union",
            "source": "ocr_blocks",
        }

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

    def _clamp_float(self, value: float, lower: float, upper: float) -> float:
        return max(lower, min(upper, value))
