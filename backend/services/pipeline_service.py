from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any

from services.document_surface_service import DocumentSurfaceService, SurfaceCrop
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
        self.document_surface_service = DocumentSurfaceService()
        self.formula_service = FormulaService()
        self.translation_service = TranslationService()

    def process_frame(self, payload: dict[str, Any]) -> dict[str, Any]:
        start = time.perf_counter()
        frame_id = payload.get("frame_id") or "frame_unknown"
        target_language = payload.get("target_language") or "vi"
        force_mock = bool(payload.get("mock", True))
        surface_image = self.document_surface_service.decode_image(payload.get("image_base64") or "")

        surface_start = time.perf_counter()
        contour_surface = self.document_surface_service.detect_from_image(surface_image)
        surface_ms = self._elapsed_ms(surface_start)
        use_surface_crop_for_ocr = payload.get("use_surface_crop_for_ocr") is True

        ocr_start = time.perf_counter()
        ocr_result = self._recognize_with_optional_surface_crop(
            payload,
            force_mock,
            surface_image,
            contour_surface if use_surface_crop_for_ocr else None,
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

        document_surface = self.document_surface_service.estimate_from_ocr_blocks(
            ocr_result.blocks,
            ocr_result.image_width,
            ocr_result.image_height,
        ) or contour_surface

        warnings = [*ocr_result.warnings, *translation_result.warnings]
        if translation_result.cache_hits:
            warnings.append(f"translation cache hits: {translation_result.cache_hits}")

        return {
            "frame_id": frame_id,
            "image_width": ocr_result.image_width,
            "image_height": ocr_result.image_height,
            "document_surface": document_surface,
            "blocks": translated_blocks,
            "provider": {
                "ocr": ocr_result.provider,
                "translation": translation_result.provider,
            },
            "mock_used": ocr_result.mock_used or translation_result.mock_used,
            "warnings": warnings,
            "latency_ms": {
                "surface_detection": surface_ms,
                "ocr": ocr_ms,
                "translation": translation_ms,
                "total": self._elapsed_ms(start),
            },
        }

    def _recognize_with_optional_surface_crop(
        self,
        payload: dict[str, Any],
        force_mock: bool,
        surface_image,
        contour_surface: dict[str, Any] | None,
    ) -> OCRResult:
        if force_mock or surface_image is None or contour_surface is None:
            return self.ocr_service.recognize(
                image_base64=payload.get("image_base64") or "",
                image_width=payload.get("image_width"),
                image_height=payload.get("image_height"),
                force_mock=force_mock,
                provider=payload.get("ocr_provider"),
            )

        surface_crop = self.document_surface_service.crop_surface(surface_image, contour_surface)
        if surface_crop is None:
            return self.ocr_service.recognize(
                image_base64=payload.get("image_base64") or "",
                image_width=payload.get("image_width"),
                image_height=payload.get("image_height"),
                force_mock=force_mock,
                provider=payload.get("ocr_provider"),
            )

        crop_result = self.ocr_service.recognize(
            image_base64=self.document_surface_service.encode_image_base64(surface_crop.image),
            image_width=surface_crop.image.width,
            image_height=surface_crop.image.height,
            force_mock=force_mock,
            provider=payload.get("ocr_provider"),
        )
        if crop_result.blocks:
            return self._map_crop_ocr_result(crop_result, surface_crop)

        fallback_result = self.ocr_service.recognize(
            image_base64=payload.get("image_base64") or "",
            image_width=payload.get("image_width"),
            image_height=payload.get("image_height"),
            force_mock=force_mock,
            provider=payload.get("ocr_provider"),
        )
        fallback_result.warnings.append("Surface crop OCR returned no blocks; retried on the original frame.")
        return fallback_result

    def _map_crop_ocr_result(self, crop_result: OCRResult, surface_crop: SurfaceCrop) -> OCRResult:
        warnings = list(crop_result.warnings)
        warnings.append("OCR ran on detected document surface crop.")
        return OCRResult(
            blocks=self.document_surface_service.map_blocks_from_crop(crop_result.blocks, surface_crop),
            image_width=surface_crop.original_width,
            image_height=surface_crop.original_height,
            provider=crop_result.provider,
            mock_used=crop_result.mock_used,
            warnings=warnings,
        )

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
        return self.document_surface_service.estimate_from_ocr_blocks(blocks, image_width, image_height)

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
