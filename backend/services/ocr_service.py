from __future__ import annotations

import base64
import io
import os
from dataclasses import dataclass
from typing import Any, Iterable

from PIL import Image

from services.errors import PipelineError


@dataclass
class OCRResult:
    blocks: list[dict[str, Any]]
    image_width: int
    image_height: int
    provider: str
    mock_used: bool
    warnings: list[str]


class OCRService:
    """OCR provider adapter.

    Mock mode is explicit. Real providers fail explicitly instead of silently
    returning mock data, so integration mistakes are visible during testing.
    """

    SUPPORTED_PROVIDERS = {"mock", "paddleocr", "tesseract"}

    def __init__(self):
        self._paddle_ocr: Any | None = None
        self._paddle_signature: tuple[str, bool] | None = None

    def recognize(
        self,
        image_base64: str,
        image_width: int | None = None,
        image_height: int | None = None,
        force_mock: bool = True,
        provider: str | None = None,
    ) -> OCRResult:
        resolved_provider = self._resolve_provider(force_mock, provider)
        image, width, height, warnings = self._decode_image_or_hints(
            image_base64=image_base64,
            image_width=image_width,
            image_height=image_height,
            require_image=resolved_provider != "mock",
        )

        if resolved_provider == "mock":
            return OCRResult(
                blocks=self._mock_blocks(width, height),
                image_width=width,
                image_height=height,
                provider="mock",
                mock_used=True,
                warnings=warnings,
            )

        if image is None:
            raise PipelineError(f"image_base64 is required for OCR_PROVIDER={resolved_provider}.")

        if resolved_provider == "paddleocr":
            blocks = self._paddleocr_blocks(image, width, height)
        elif resolved_provider == "tesseract":
            blocks = self._tesseract_blocks(image, width, height)
        else:
            raise PipelineError(f"Unsupported OCR provider: {resolved_provider}")

        if not blocks:
            warnings.append(f"OCR provider '{resolved_provider}' returned no text blocks.")

        return OCRResult(
            blocks=blocks,
            image_width=width,
            image_height=height,
            provider=resolved_provider,
            mock_used=False,
            warnings=warnings,
        )

    def _resolve_provider(self, force_mock: bool, provider: str | None) -> str:
        if force_mock:
            return "mock"

        selected = (provider or os.getenv("OCR_PROVIDER") or "paddleocr").strip().lower()
        if selected not in self.SUPPORTED_PROVIDERS:
            raise PipelineError(
                f"Unsupported OCR provider '{selected}'. Expected one of: {sorted(self.SUPPORTED_PROVIDERS)}."
            )
        return selected

    def _decode_image_or_hints(
        self,
        image_base64: str,
        image_width: int | None,
        image_height: int | None,
        require_image: bool,
    ) -> tuple[Image.Image | None, int, int, list[str]]:
        warnings: list[str] = []
        if image_base64:
            encoded = image_base64.split(",", 1)[1] if "," in image_base64[:64] else image_base64
            try:
                image_bytes = base64.b64decode(encoded, validate=True)
                image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
                image.load()
            except Exception as exc:
                raise PipelineError(f"Invalid image_base64 payload: {exc}") from exc

            width, height = image.size
            if image_width and int(image_width) != width:
                warnings.append(f"image_width hint {image_width} did not match decoded width {width}.")
            if image_height and int(image_height) != height:
                warnings.append(f"image_height hint {image_height} did not match decoded height {height}.")
            return image, width, height, warnings

        if require_image:
            raise PipelineError("image_base64 is required when mock=false.")

        width = int(image_width or 1280)
        height = int(image_height or 720)
        if not image_width or not image_height:
            warnings.append("No image was provided in mock mode; using fallback image size.")
        return None, width, height, warnings

    def _mock_blocks(self, width: int, height: int) -> list[dict[str, Any]]:
        return [
            {
                "id": "b1",
                "text": "Deep learning uses neural networks.",
                "bbox": [int(width * 0.16), int(height * 0.25), int(width * 0.84), int(height * 0.33)],
                "confidence": 0.98,
            },
            {
                "id": "b2",
                "text": "The loss is L = -Σ y log(p).",
                "bbox": [int(width * 0.16), int(height * 0.39), int(width * 0.84), int(height * 0.47)],
                "confidence": 0.95,
            },
            {
                "id": "b3",
                "text": "Gradient descent updates the model weights.",
                "bbox": [int(width * 0.16), int(height * 0.53), int(width * 0.84), int(height * 0.61)],
                "confidence": 0.96,
            },
        ]

    def _paddleocr_blocks(self, image: Image.Image, width: int, height: int) -> list[dict[str, Any]]:
        try:
            import numpy as np  # type: ignore
        except Exception as exc:
            raise PipelineError("PaddleOCR provider requires numpy in the active environment.") from exc

        ocr = self._get_paddle_ocr()
        min_confidence = self._min_confidence()

        try:
            image_array = np.array(image)
            if hasattr(ocr, "predict"):
                raw_result = ocr.predict(
                    image_array,
                    use_doc_orientation_classify=False,
                    use_doc_unwarping=False,
                    use_textline_orientation=True,
                )
            else:
                raw_result = ocr.ocr(image_array)
        except Exception as exc:
            raise PipelineError(f"PaddleOCR failed: {exc}", status_code=500, code="ocr_provider_failed") from exc

        return self.normalize_paddle_output(raw_result, width, height, min_confidence)

    def _get_paddle_ocr(self) -> Any:
        lang = os.getenv("PADDLEOCR_LANG", "en")
        use_gpu = os.getenv("PADDLEOCR_USE_GPU", "1") == "1"
        signature = (lang, use_gpu)
        if self._paddle_ocr is not None and self._paddle_signature == signature:
            return self._paddle_ocr

        try:
            from paddleocr import PaddleOCR  # type: ignore
        except Exception as exc:
            raise PipelineError(
                "OCR_PROVIDER=paddleocr requires the backend to run inside the paddleocr_gpu environment."
            ) from exc

        device = os.getenv("PADDLEOCR_DEVICE", "gpu" if use_gpu else "cpu")
        kwargs: dict[str, Any] = {
            "lang": lang,
            "use_doc_orientation_classify": False,
            "use_doc_unwarping": False,
            "use_textline_orientation": True,
            "device": device,
        }

        try:
            self._paddle_ocr = PaddleOCR(**kwargs)
        except TypeError:
            legacy_kwargs = {"lang": lang, "use_angle_cls": True, "use_gpu": use_gpu}
            try:
                self._paddle_ocr = PaddleOCR(**legacy_kwargs)
            except Exception as exc:
                raise PipelineError(
                    f"Could not initialize PaddleOCR with lang={lang}, use_gpu={use_gpu}: {exc}",
                    status_code=500,
                    code="ocr_provider_init_failed",
                ) from exc
        except Exception as exc:
            raise PipelineError(
                f"Could not initialize PaddleOCR with lang={lang}, device={device}: {exc}",
                status_code=500,
                code="ocr_provider_init_failed",
            ) from exc

        self._paddle_signature = signature
        return self._paddle_ocr

    def normalize_paddle_output(
        self,
        raw_result: Any,
        image_width: int,
        image_height: int,
        min_confidence: float | None = None,
    ) -> list[dict[str, Any]]:
        threshold = self._min_confidence() if min_confidence is None else min_confidence
        blocks: list[dict[str, Any]] = []

        for polygon, text, confidence in self._iter_ocr_items(raw_result):
            text = str(text).strip()
            if not text:
                continue

            conf = self._safe_float(confidence, default=0.0)
            if conf > 1.0:
                conf = conf / 100.0
            conf = max(0.0, min(1.0, conf))
            if conf < threshold:
                continue

            bbox = self._polygon_to_bbox(polygon, image_width, image_height)
            if bbox is None:
                continue

            blocks.append({
                "id": f"ocr_{len(blocks) + 1}",
                "text": text,
                "bbox": bbox,
                "confidence": conf,
            })

        return blocks

    def _iter_ocr_items(self, value: Any) -> Iterable[tuple[Any, str, float]]:
        value = self._to_plain_data(value)

        if isinstance(value, dict):
            texts = value.get("rec_texts") or value.get("texts") or value.get("text")
            scores = value.get("rec_scores") or value.get("scores") or value.get("confidence")
            polygons = (
                value.get("rec_polys")
                or value.get("dt_polys")
                or value.get("det_polys")
                or value.get("boxes")
                or value.get("polys")
            )
            if isinstance(texts, list) and isinstance(polygons, list):
                if not isinstance(scores, list):
                    scores = [1.0] * len(texts)
                for polygon, text, score in zip(polygons, texts, scores):
                    yield polygon, str(text), self._safe_float(score, default=0.0)
                return

            for nested in value.values():
                yield from self._iter_ocr_items(nested)
            return

        if isinstance(value, (list, tuple)):
            if self._looks_like_old_paddle_line(value):
                text_part = value[1]
                text = text_part[0]
                score = text_part[1] if len(text_part) > 1 else 1.0
                yield value[0], str(text), self._safe_float(score, default=0.0)
                return

            for nested in value:
                yield from self._iter_ocr_items(nested)

    def _to_plain_data(self, value: Any) -> Any:
        if isinstance(value, (dict, list, tuple, str, int, float, type(None))):
            return value
        if hasattr(value, "to_dict"):
            try:
                return value.to_dict()
            except Exception:
                pass
        if hasattr(value, "json"):
            try:
                json_value = value.json
                return json_value() if callable(json_value) else json_value
            except Exception:
                pass
        if hasattr(value, "__dict__"):
            return vars(value)
        return value

    def _looks_like_old_paddle_line(self, value: Any) -> bool:
        if not isinstance(value, (list, tuple)) or len(value) < 2:
            return False
        text_part = value[1]
        return (
            isinstance(text_part, (list, tuple))
            and len(text_part) >= 1
            and isinstance(text_part[0], str)
        )

    def _polygon_to_bbox(self, polygon: Any, image_width: int, image_height: int) -> list[int] | None:
        points = self._polygon_points(polygon)
        if not points:
            return None

        xs = [point[0] for point in points]
        ys = [point[1] for point in points]
        x1 = self._clamp(round(min(xs)), 0, image_width)
        y1 = self._clamp(round(min(ys)), 0, image_height)
        x2 = self._clamp(round(max(xs)), 0, image_width)
        y2 = self._clamp(round(max(ys)), 0, image_height)
        if x2 <= x1 or y2 <= y1:
            return None
        return [x1, y1, x2, y2]

    def _polygon_points(self, polygon: Any) -> list[tuple[float, float]]:
        polygon = self._to_plain_data(polygon)
        if hasattr(polygon, "tolist"):
            polygon = polygon.tolist()

        if isinstance(polygon, (list, tuple)) and polygon and all(self._is_number(v) for v in polygon):
            numbers = [float(v) for v in polygon]
            return list(zip(numbers[0::2], numbers[1::2]))

        points: list[tuple[float, float]] = []
        if isinstance(polygon, (list, tuple)):
            for point in polygon:
                if hasattr(point, "tolist"):
                    point = point.tolist()
                if isinstance(point, (list, tuple)) and len(point) >= 2:
                    try:
                        points.append((float(point[0]), float(point[1])))
                    except (TypeError, ValueError):
                        continue
        return points

    def _tesseract_blocks(self, image: Image.Image, width: int, height: int) -> list[dict[str, Any]]:
        try:
            import pytesseract  # type: ignore
        except Exception as exc:
            raise PipelineError("OCR_PROVIDER=tesseract requires pytesseract to be installed.") from exc

        cmd = os.getenv("TESSERACT_CMD")
        if cmd:
            pytesseract.pytesseract.tesseract_cmd = cmd

        try:
            data = pytesseract.image_to_data(image, output_type=pytesseract.Output.DICT)
        except Exception as exc:
            raise PipelineError(f"Tesseract failed: {exc}", status_code=500, code="ocr_provider_failed") from exc

        lines: dict[tuple[int, int, int], list[int]] = {}
        n = len(data.get("text", []))
        for i in range(n):
            text = data["text"][i].strip()
            if not text:
                continue
            conf = self._safe_float(data["conf"][i], default=-1.0)
            if conf < self._min_confidence() * 100:
                continue
            key = (data["block_num"][i], data["par_num"][i], data["line_num"][i])
            lines.setdefault(key, []).append(i)

        blocks: list[dict[str, Any]] = []
        for _, indices in lines.items():
            words = [data["text"][i].strip() for i in indices if data["text"][i].strip()]
            if not words:
                continue

            x1 = min(data["left"][i] for i in indices)
            y1 = min(data["top"][i] for i in indices)
            x2 = max(data["left"][i] + data["width"][i] for i in indices)
            y2 = max(data["top"][i] + data["height"][i] for i in indices)
            bbox = [
                self._clamp(x1, 0, width),
                self._clamp(y1, 0, height),
                self._clamp(x2, 0, width),
                self._clamp(y2, 0, height),
            ]
            if bbox[2] <= bbox[0] or bbox[3] <= bbox[1]:
                continue

            confs = [self._safe_float(data["conf"][i], default=0.0) for i in indices]
            confidence = max(0.0, min(1.0, (sum(confs) / len(confs) / 100.0) if confs else 0.0))
            blocks.append({
                "id": f"ocr_{len(blocks) + 1}",
                "text": " ".join(words),
                "bbox": bbox,
                "confidence": confidence,
            })

        return blocks

    def _min_confidence(self) -> float:
        return max(0.0, min(1.0, self._safe_float(os.getenv("OCR_MIN_CONFIDENCE"), default=0.45)))

    def _safe_float(self, value: Any, default: float) -> float:
        try:
            return float(value)
        except (TypeError, ValueError):
            return default

    def _is_number(self, value: Any) -> bool:
        return isinstance(value, (int, float)) and not isinstance(value, bool)

    def _clamp(self, value: int, lower: int, upper: int) -> int:
        return max(lower, min(upper, int(value)))
