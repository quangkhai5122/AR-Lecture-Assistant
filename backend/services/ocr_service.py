from __future__ import annotations

import base64
import io
import os
from dataclasses import dataclass
from typing import Any, Iterable

import requests
from PIL import Image, ImageEnhance, ImageFilter, ImageOps

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

    SUPPORTED_PROVIDERS = {"mock", "paddleocr", "tesseract", "google"}

    def __init__(self):
        self._paddle_ocr: Any | None = None
        self._paddle_signature: tuple[Any, ...] | None = None

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

        provider_used = resolved_provider
        try:
            blocks = self._recognize_with_provider(resolved_provider, image, width, height)
        except PipelineError as exc:
            fallback_provider = self._fallback_provider_for(resolved_provider, exc)
            if fallback_provider is None:
                raise

            warnings.append(
                f"OCR provider '{resolved_provider}' unavailable ({exc.code}); "
                f"falling back to '{fallback_provider}'."
            )
            try:
                blocks = self._recognize_with_provider(fallback_provider, image, width, height)
            except PipelineError as fallback_exc:
                raise PipelineError(
                    f"OCR provider '{resolved_provider}' failed and fallback "
                    f"'{fallback_provider}' also failed: {fallback_exc.message}",
                    status_code=fallback_exc.status_code,
                    code=fallback_exc.code,
                ) from fallback_exc
            provider_used = fallback_provider

        blocks = self._postprocess_blocks(blocks, width, height)

        if not blocks:
            warnings.append(f"OCR provider '{provider_used}' returned no text blocks.")

        return OCRResult(
            blocks=blocks,
            image_width=width,
            image_height=height,
            provider=provider_used,
            mock_used=False,
            warnings=warnings,
        )

    def _resolve_provider(self, force_mock: bool, provider: str | None) -> str:
        if force_mock:
            return "mock"

        selected = (provider or os.getenv("OCR_PROVIDER") or "google").strip().lower()
        if selected not in self.SUPPORTED_PROVIDERS:
            raise PipelineError(
                f"Unsupported OCR provider '{selected}'. Expected one of: {sorted(self.SUPPORTED_PROVIDERS)}."
            )
        return selected

    def _recognize_with_provider(
        self,
        provider: str,
        image: Image.Image,
        width: int,
        height: int,
    ) -> list[dict[str, Any]]:
        if provider == "paddleocr":
            return self._paddleocr_blocks(image, width, height)
        if provider == "tesseract":
            return self._tesseract_blocks(image, width, height)
        if provider == "google":
            return self._google_vision_blocks(image, width, height)
        raise PipelineError(f"Unsupported OCR provider: {provider}")

    def _fallback_provider_for(self, provider: str, error: PipelineError) -> str | None:
        if os.getenv("OCR_ENABLE_PROVIDER_FALLBACK", "1") != "1":
            return None
        if provider != "paddleocr":
            return None
        if error.code not in {"ocr_provider_not_available", "ocr_provider_init_failed"}:
            return None

        fallback = (os.getenv("OCR_FALLBACK_PROVIDER") or "tesseract").strip().lower()
        if fallback == provider or fallback not in self.SUPPORTED_PROVIDERS:
            return None
        if fallback == "mock" and os.getenv("OCR_ALLOW_MOCK_FALLBACK", "0") != "1":
            return None
        return fallback

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
            raise PipelineError(
                "PaddleOCR provider requires numpy in the active environment.",
                status_code=503,
                code="ocr_provider_not_available",
            ) from exc

        ocr = self._get_paddle_ocr()
        min_confidence = self._min_confidence()
        ocr_image, scale_x, scale_y = self._prepare_ocr_image(image)

        try:
            image_array = np.array(ocr_image)
            if hasattr(ocr, "predict"):
                predict_kwargs = {
                    "use_doc_orientation_classify": False,
                    "use_doc_unwarping": False,
                    "use_textline_orientation": True,
                    **self._paddle_detection_parameters(),
                }
                try:
                    raw_result = ocr.predict(image_array, **predict_kwargs)
                except TypeError:
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

        return self.normalize_paddle_output(
            raw_result,
            width,
            height,
            min_confidence,
            scale_x=scale_x,
            scale_y=scale_y,
        )

    def _get_paddle_ocr(self) -> Any:
        lang = os.getenv("PADDLEOCR_LANG", "en")
        use_gpu = os.getenv("PADDLEOCR_USE_GPU", "1") == "1"
        detection_signature = tuple(sorted(self._paddle_detection_parameters().items()))
        signature = (lang, use_gpu, detection_signature)
        if self._paddle_ocr is not None and self._paddle_signature == signature:
            return self._paddle_ocr

        try:
            from paddleocr import PaddleOCR  # type: ignore
        except Exception as exc:
            raise PipelineError(
                "OCR_PROVIDER=paddleocr requires the backend to run inside the paddleocr_gpu environment.",
                status_code=503,
                code="ocr_provider_not_available",
            ) from exc

        device = os.getenv("PADDLEOCR_DEVICE", "gpu" if use_gpu else "cpu")
        modern_base_kwargs: dict[str, Any] = {
            "lang": lang,
            "use_doc_orientation_classify": False,
            "use_doc_unwarping": False,
            "use_textline_orientation": True,
            "device": device,
        }
        legacy_base_kwargs: dict[str, Any] = {"lang": lang, "use_angle_cls": True, "use_gpu": use_gpu}
        attempts: list[tuple[str, dict[str, Any]]] = [
            ("modern", {**modern_base_kwargs, **self._paddle_detection_parameters()}),
            ("modern_without_detection_tuning", modern_base_kwargs),
            ("legacy", {**legacy_base_kwargs, **self._paddle_detection_parameters(legacy=True)}),
            ("legacy_without_detection_tuning", legacy_base_kwargs),
        ]

        last_type_error: TypeError | None = None
        for attempt_name, attempt_kwargs in attempts:
            try:
                self._paddle_ocr = PaddleOCR(**attempt_kwargs)
                self._paddle_signature = signature
                return self._paddle_ocr
            except TypeError as exc:
                last_type_error = exc
            except Exception as exc:
                raise PipelineError(
                    f"Could not initialize PaddleOCR ({attempt_name}) with lang={lang}, device={device}: {exc}",
                    status_code=500,
                    code="ocr_provider_init_failed",
                ) from exc

        raise PipelineError(
            f"Could not initialize PaddleOCR with supported argument sets: {last_type_error}",
            status_code=500,
            code="ocr_provider_init_failed",
        )

    def _paddle_detection_parameters(self, legacy: bool = False) -> dict[str, Any]:
        limit_side_len = max(0, int(self._safe_float(os.getenv("PADDLEOCR_DET_LIMIT_SIDE_LEN"), default=1536)))
        limit_type = (os.getenv("PADDLEOCR_DET_LIMIT_TYPE", "min") or "min").strip().lower()
        if limit_type not in {"min", "max"}:
            limit_type = "min"

        box_thresh = self._safe_float(os.getenv("PADDLEOCR_DET_BOX_THRESH"), default=0.30)
        unclip_raw = os.getenv("PADDLEOCR_DET_UNCLIP_RATIO")

        if legacy:
            parameters: dict[str, Any] = {
                "det_limit_type": limit_type,
            }
            if limit_side_len > 0:
                parameters["det_limit_side_len"] = limit_side_len
            if box_thresh > 0:
                parameters["det_db_box_thresh"] = box_thresh
            if unclip_raw:
                parameters["det_db_unclip_ratio"] = self._safe_float(unclip_raw, default=1.5)
            return parameters

        parameters = {
            "text_det_limit_type": limit_type,
        }
        if limit_side_len > 0:
            parameters["text_det_limit_side_len"] = limit_side_len
        if box_thresh > 0:
            parameters["text_det_box_thresh"] = box_thresh
        if unclip_raw:
            parameters["text_det_unclip_ratio"] = self._safe_float(unclip_raw, default=2.0)
        return parameters

    def normalize_paddle_output(
        self,
        raw_result: Any,
        image_width: int,
        image_height: int,
        min_confidence: float | None = None,
        scale_x: float = 1.0,
        scale_y: float = 1.0,
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

            bbox = self._polygon_to_bbox(
                polygon,
                image_width,
                image_height,
                scale_x=scale_x,
                scale_y=scale_y,
            )
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

    def _polygon_to_bbox(
        self,
        polygon: Any,
        image_width: int,
        image_height: int,
        scale_x: float = 1.0,
        scale_y: float = 1.0,
    ) -> list[int] | None:
        points = self._polygon_points(polygon)
        if not points:
            return None

        scale_x = max(0.0001, scale_x)
        scale_y = max(0.0001, scale_y)
        xs = [point[0] / scale_x for point in points]
        ys = [point[1] / scale_y for point in points]
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
            raise PipelineError(
                "OCR_PROVIDER=tesseract requires pytesseract to be installed.",
                status_code=503,
                code="ocr_provider_not_available",
            ) from exc

        cmd = os.getenv("TESSERACT_CMD")
        if cmd:
            pytesseract.pytesseract.tesseract_cmd = cmd

        ocr_image, scale_x, scale_y = self._prepare_ocr_image(image)

        try:
            data = pytesseract.image_to_data(ocr_image, output_type=pytesseract.Output.DICT)
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

            x1 = min(data["left"][i] for i in indices) / scale_x
            y1 = min(data["top"][i] for i in indices) / scale_y
            x2 = max(data["left"][i] + data["width"][i] for i in indices) / scale_x
            y2 = max(data["top"][i] + data["height"][i] for i in indices) / scale_y
            bbox = [
                self._clamp(round(x1), 0, width),
                self._clamp(round(y1), 0, height),
                self._clamp(round(x2), 0, width),
                self._clamp(round(y2), 0, height),
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

    def _google_vision_blocks(self, image: Image.Image, width: int, height: int) -> list[dict[str, Any]]:
        api_key = (
            os.getenv("GOOGLE_VISION_API_KEY")
            or os.getenv("GOOGLE_CLOUD_VISION_API_KEY")
            or os.getenv("GOOGLE_CLOUD_API_KEY")
        )
        if not api_key:
            raise PipelineError(
                "OCR_PROVIDER=google requires GOOGLE_VISION_API_KEY or GOOGLE_CLOUD_API_KEY.",
                status_code=503,
                code="ocr_provider_not_configured",
            )

        buffer = io.BytesIO()
        image.save(buffer, format="PNG")
        image_content = base64.b64encode(buffer.getvalue()).decode("utf-8")

        request_item: dict[str, Any] = {
            "image": {"content": image_content},
            "features": [{"type": os.getenv("GOOGLE_VISION_FEATURE", "DOCUMENT_TEXT_DETECTION")}],
        }
        language_hints = [
            item.strip()
            for item in os.getenv("GOOGLE_VISION_LANGUAGE_HINTS", "en").split(",")
            if item.strip()
        ]
        if language_hints:
            request_item["imageContext"] = {"languageHints": language_hints}

        url = os.getenv("GOOGLE_VISION_URL", "https://vision.googleapis.com/v1/images:annotate")
        timeout = float(os.getenv("GOOGLE_VISION_TIMEOUT_SECONDS", "20"))
        try:
            response = requests.post(
                url,
                params={"key": api_key},
                json={"requests": [request_item]},
                timeout=timeout,
            )
            response.raise_for_status()
            data = response.json()
        except Exception as exc:
            raise PipelineError(
                f"Google Vision OCR request failed: {exc}",
                status_code=502,
                code="ocr_provider_failed",
            ) from exc

        responses = data.get("responses") if isinstance(data, dict) else None
        if not isinstance(responses, list) or not responses:
            raise PipelineError(
                f"Unexpected Google Vision response shape: {data}",
                status_code=502,
                code="ocr_provider_bad_response",
            )

        vision_response = responses[0]
        if not isinstance(vision_response, dict):
            raise PipelineError(
                f"Unexpected Google Vision response shape: {data}",
                status_code=502,
                code="ocr_provider_bad_response",
            )
        if isinstance(vision_response.get("error"), dict):
            message = vision_response["error"].get("message") or vision_response["error"]
            raise PipelineError(
                f"Google Vision OCR failed: {message}",
                status_code=502,
                code="ocr_provider_failed",
            )

        return (
            self._google_full_text_blocks(vision_response.get("fullTextAnnotation"), width, height)
            or self._google_text_annotation_blocks(vision_response.get("textAnnotations"), width, height)
        )

    def _google_full_text_blocks(
        self,
        annotation: Any,
        image_width: int,
        image_height: int,
    ) -> list[dict[str, Any]]:
        if not isinstance(annotation, dict):
            return []

        blocks: list[dict[str, Any]] = []
        pages = annotation.get("pages")
        if not isinstance(pages, list):
            return []

        for page in pages:
            for block in self._safe_list(page.get("blocks") if isinstance(page, dict) else None):
                for paragraph in self._safe_list(block.get("paragraphs") if isinstance(block, dict) else None):
                    for word in self._safe_list(paragraph.get("words") if isinstance(paragraph, dict) else None):
                        text = self._google_word_text(word)
                        if not text:
                            continue
                        bbox = self._google_bounding_poly_to_bbox(
                            word.get("boundingBox") if isinstance(word, dict) else None,
                            image_width,
                            image_height,
                        )
                        if bbox is None:
                            continue

                        blocks.append({
                            "id": f"ocr_{len(blocks) + 1}",
                            "text": text,
                            "bbox": bbox,
                            "confidence": round(max(0.0, min(1.0, self._safe_float(word.get("confidence"), default=1.0))), 4),
                        })

        return blocks

    def _google_text_annotation_blocks(
        self,
        annotations: Any,
        image_width: int,
        image_height: int,
    ) -> list[dict[str, Any]]:
        if not isinstance(annotations, list):
            return []

        blocks: list[dict[str, Any]] = []
        for annotation in annotations[1:]:
            if not isinstance(annotation, dict):
                continue
            text = str(annotation.get("description", "")).strip()
            if not text:
                continue
            bbox = self._google_bounding_poly_to_bbox(annotation.get("boundingPoly"), image_width, image_height)
            if bbox is None:
                continue
            blocks.append({
                "id": f"ocr_{len(blocks) + 1}",
                "text": text,
                "bbox": bbox,
                "confidence": self._safe_float(annotation.get("score"), default=1.0),
            })

        return blocks

    def _google_word_text(self, word: Any) -> str:
        if not isinstance(word, dict):
            return ""
        symbols = word.get("symbols")
        if not isinstance(symbols, list):
            return ""
        return "".join(
            str(symbol.get("text", ""))
            for symbol in symbols
            if isinstance(symbol, dict)
        ).strip()

    def _google_bounding_poly_to_bbox(
        self,
        bounding_poly: Any,
        image_width: int,
        image_height: int,
    ) -> list[int] | None:
        if not isinstance(bounding_poly, dict):
            return None

        vertices = bounding_poly.get("vertices") or bounding_poly.get("normalizedVertices")
        if not isinstance(vertices, list) or not vertices:
            return None

        points: list[tuple[float, float]] = []
        normalized = "normalizedVertices" in bounding_poly
        for vertex in vertices:
            if not isinstance(vertex, dict):
                continue
            x = self._safe_float(vertex.get("x"), default=0.0)
            y = self._safe_float(vertex.get("y"), default=0.0)
            if normalized:
                x *= image_width
                y *= image_height
            points.append((x, y))

        if not points:
            return None

        x1 = self._clamp(round(min(point[0] for point in points)), 0, image_width)
        y1 = self._clamp(round(min(point[1] for point in points)), 0, image_height)
        x2 = self._clamp(round(max(point[0] for point in points)), 0, image_width)
        y2 = self._clamp(round(max(point[1] for point in points)), 0, image_height)
        if x2 <= x1 or y2 <= y1:
            return None
        return [x1, y1, x2, y2]

    def _safe_list(self, value: Any) -> list[Any]:
        return value if isinstance(value, list) else []

    def _min_confidence(self) -> float:
        return max(0.0, min(1.0, self._safe_float(os.getenv("OCR_MIN_CONFIDENCE"), default=0.22)))

    def _prepare_ocr_image(self, image: Image.Image) -> tuple[Image.Image, float, float]:
        if os.getenv("OCR_PREPROCESS_ENABLED", "1") != "1":
            return image, 1.0, 1.0

        width, height = image.size
        longest_side = max(width, height)
        if width <= 0 or height <= 0 or longest_side <= 0:
            return image, 1.0, 1.0

        target_long_side = max(0, int(self._safe_float(os.getenv("OCR_UPSCALE_LONG_SIDE"), default=3200)))
        max_upscale = max(1.0, self._safe_float(os.getenv("OCR_MAX_UPSCALE"), default=4.0))
        scale = 1.0
        if target_long_side > longest_side:
            scale = min(max_upscale, target_long_side / float(longest_side))

        prepared = image
        if scale > 1.01:
            target_size = (
                max(1, round(width * scale)),
                max(1, round(height * scale)),
            )
            prepared = prepared.resize(target_size, Image.Resampling.LANCZOS)

        if os.getenv("OCR_GRAYSCALE", "1") == "1":
            prepared = ImageOps.grayscale(prepared).convert("RGB")

        if os.getenv("OCR_AUTOCONTRAST", "1") == "1":
            cutoff = max(0.0, min(10.0, self._safe_float(os.getenv("OCR_AUTOCONTRAST_CUTOFF"), default=1.0)))
            prepared = ImageOps.autocontrast(prepared, cutoff=cutoff)

        contrast = self._safe_float(os.getenv("OCR_CONTRAST"), default=1.35)
        if contrast > 0 and abs(contrast - 1.0) > 0.01:
            prepared = ImageEnhance.Contrast(prepared).enhance(contrast)

        sharpness = self._safe_float(os.getenv("OCR_SHARPNESS"), default=1.45)
        if sharpness > 0 and abs(sharpness - 1.0) > 0.01:
            prepared = ImageEnhance.Sharpness(prepared).enhance(sharpness)

        if os.getenv("OCR_UNSHARP_MASK", "1") == "1":
            radius = self._safe_float(os.getenv("OCR_UNSHARP_RADIUS"), default=1.1)
            percent = int(self._safe_float(os.getenv("OCR_UNSHARP_PERCENT"), default=160))
            threshold = int(self._safe_float(os.getenv("OCR_UNSHARP_THRESHOLD"), default=2))
            prepared = prepared.filter(ImageFilter.UnsharpMask(radius=radius, percent=percent, threshold=threshold))

        if os.getenv("OCR_THRESHOLD_ENABLED", "0") == "1":
            threshold_value = int(max(0, min(255, self._safe_float(os.getenv("OCR_THRESHOLD_VALUE"), default=180))))
            prepared = ImageOps.grayscale(prepared).point(
                lambda pixel: 255 if pixel >= threshold_value else 0
            ).convert("RGB")

        prepared_width, prepared_height = prepared.size
        return prepared, prepared_width / float(width), prepared_height / float(height)

    def _postprocess_blocks(
        self,
        blocks: list[dict[str, Any]],
        image_width: int,
        image_height: int,
    ) -> list[dict[str, Any]]:
        if not blocks:
            return []

        cleaned = self._clean_ocr_blocks(blocks, image_width, image_height)
        if os.getenv("OCR_MERGE_TEXT_LINES", "1") != "1" or len(cleaned) <= 1:
            return self._renumber_blocks(cleaned)

        return self._renumber_blocks(self._merge_same_line_blocks(cleaned, image_width))

    def _clean_ocr_blocks(
        self,
        blocks: list[dict[str, Any]],
        image_width: int,
        image_height: int,
    ) -> list[dict[str, Any]]:
        cleaned: list[dict[str, Any]] = []
        min_area_ratio = max(0.0, self._safe_float(os.getenv("OCR_MIN_BLOCK_AREA_RATIO"), default=0.0))
        image_area = max(1.0, float(image_width * image_height))

        for block in blocks:
            text = " ".join(str(block.get("text", "")).split())
            bbox = block.get("bbox")
            if not text or not isinstance(bbox, list) or len(bbox) < 4:
                continue

            try:
                x1, y1, x2, y2 = [float(value) for value in bbox[:4]]
            except (TypeError, ValueError):
                continue

            left = self._clamp(round(min(x1, x2)), 0, image_width)
            top = self._clamp(round(min(y1, y2)), 0, image_height)
            right = self._clamp(round(max(x1, x2)), 0, image_width)
            bottom = self._clamp(round(max(y1, y2)), 0, image_height)
            x1, y1, x2, y2 = left, top, right, bottom
            if x2 <= x1 or y2 <= y1:
                continue
            if min_area_ratio > 0 and ((x2 - x1) * (y2 - y1)) / image_area < min_area_ratio:
                continue

            cleaned.append({
                "id": str(block.get("id") or f"ocr_{len(cleaned) + 1}"),
                "text": text,
                "bbox": [x1, y1, x2, y2],
                "confidence": max(0.0, min(1.0, self._safe_float(block.get("confidence"), default=0.0))),
            })

        cleaned.sort(key=lambda item: (item["bbox"][1], item["bbox"][0]))
        return cleaned

    def _merge_same_line_blocks(self, blocks: list[dict[str, Any]], image_width: int) -> list[dict[str, Any]]:
        lines: list[list[dict[str, Any]]] = []
        for block in blocks:
            best_line: list[dict[str, Any]] | None = None
            best_distance = float("inf")
            for line in lines:
                center_distance = [float("inf")]
                if self._can_join_line(block, line, image_width, center_distance):
                    if center_distance[0] < best_distance:
                        best_distance = center_distance[0]
                        best_line = line

            if best_line is None:
                best_line = []
                lines.append(best_line)

            best_line.append(block)
            best_line.sort(key=lambda item: item["bbox"][0])

        merged = [self._merge_line(line) for line in lines if line]
        merged.sort(key=lambda item: (item["bbox"][1], item["bbox"][0]))
        return merged

    def _can_join_line(
        self,
        block: dict[str, Any],
        line: list[dict[str, Any]],
        image_width: int,
        center_distance_out: list[float],
    ) -> bool:
        line_bbox = self._union_bbox([item["bbox"] for item in line])
        bbox = block["bbox"]
        line_height = max(1.0, line_bbox[3] - line_bbox[1])
        block_height = max(1.0, bbox[3] - bbox[1])
        min_height = max(1.0, min(line_height, block_height))

        vertical_overlap = max(0.0, min(line_bbox[3], bbox[3]) - max(line_bbox[1], bbox[1]))
        overlap_ratio = vertical_overlap / min_height
        center_distance = abs(((line_bbox[1] + line_bbox[3]) * 0.5) - ((bbox[1] + bbox[3]) * 0.5))
        center_distance_out[0] = center_distance

        horizontal_gap = 0.0
        if bbox[0] > line_bbox[2]:
            horizontal_gap = bbox[0] - line_bbox[2]
        elif line_bbox[0] > bbox[2]:
            horizontal_gap = line_bbox[0] - bbox[2]

        max_gap_ratio = self._safe_float(os.getenv("OCR_LINE_MERGE_MAX_GAP_RATIO"), default=0.035)
        max_gap = max(image_width * max_gap_ratio, max(line_height, block_height) * 3.2)
        return (overlap_ratio >= 0.42 or center_distance <= max(line_height, block_height) * 0.62) and horizontal_gap <= max_gap

    def _merge_line(self, line: list[dict[str, Any]]) -> dict[str, Any]:
        line.sort(key=lambda item: item["bbox"][0])
        bbox = self._union_bbox([item["bbox"] for item in line])
        text = " ".join(str(item["text"]).strip() for item in line if str(item.get("text", "")).strip())
        confidences = [float(item.get("confidence", 0.0)) for item in line]
        confidence = sum(confidences) / len(confidences) if confidences else 0.0
        return {
            "id": str(line[0].get("id") or "ocr_1"),
            "text": text,
            "bbox": bbox,
            "confidence": round(max(0.0, min(1.0, confidence)), 4),
        }

    def _union_bbox(self, boxes: list[list[int]]) -> list[int]:
        return [
            min(box[0] for box in boxes),
            min(box[1] for box in boxes),
            max(box[2] for box in boxes),
            max(box[3] for box in boxes),
        ]

    def _renumber_blocks(self, blocks: list[dict[str, Any]]) -> list[dict[str, Any]]:
        renumbered: list[dict[str, Any]] = []
        for index, block in enumerate(blocks, start=1):
            item = dict(block)
            item["id"] = f"ocr_{index}"
            renumbered.append(item)
        return renumbered

    def _safe_float(self, value: Any, default: float) -> float:
        try:
            return float(value)
        except (TypeError, ValueError):
            return default

    def _is_number(self, value: Any) -> bool:
        return isinstance(value, (int, float)) and not isinstance(value, bool)

    def _clamp(self, value: int, lower: int, upper: int) -> int:
        return max(lower, min(upper, int(value)))
