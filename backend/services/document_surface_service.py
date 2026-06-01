from __future__ import annotations

import base64
import io
from dataclasses import dataclass
from typing import Any

import numpy as np
from PIL import Image, ImageFilter


@dataclass(frozen=True)
class SurfaceCrop:
    image: Image.Image
    x_offset: int
    y_offset: int
    original_width: int
    original_height: int
    source_corners: tuple[tuple[float, float], tuple[float, float], tuple[float, float], tuple[float, float]]
    perspective_coefficients: tuple[float, float, float, float, float, float, float, float] | None = None


class DocumentSurfaceService:
    """Detects the board/slide surface and falls back to OCR bbox union.

    The detector intentionally avoids a hard OpenCV dependency so the backend
    test environment stays lightweight. It uses grayscale smoothing, simple
    edge energy, and contour-like bounding geometry for the demo path.
    """

    def decode_image(self, image_base64: str | None) -> Image.Image | None:
        if not image_base64:
            return None

        encoded = image_base64.split(",", 1)[1] if "," in image_base64[:64] else image_base64
        try:
            image_bytes = base64.b64decode(encoded, validate=True)
            image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
            image.load()
            return image
        except Exception:
            return None

    def encode_image_base64(self, image: Image.Image, image_format: str = "PNG") -> str:
        buffer = io.BytesIO()
        image.save(buffer, format=image_format)
        return base64.b64encode(buffer.getvalue()).decode("utf-8")

    def detect(
        self,
        image: Image.Image | None,
        ocr_blocks: list[dict[str, Any]],
        image_width: int,
        image_height: int,
    ) -> dict[str, Any] | None:
        contour_surface = self.detect_from_image(image)
        fallback_surface = self.estimate_from_ocr_blocks(ocr_blocks, image_width, image_height)

        if contour_surface is not None:
            return contour_surface
        return fallback_surface

    def detect_from_image(self, image: Image.Image | None) -> dict[str, Any] | None:
        if image is None:
            return None

        width, height = image.size
        if width <= 0 or height <= 0:
            return None

        grayscale = image.convert("L").filter(ImageFilter.GaussianBlur(radius=1.2))
        pixels = np.asarray(grayscale, dtype=np.float32)
        if pixels.size == 0:
            return None

        gradient_x = np.zeros_like(pixels)
        gradient_y = np.zeros_like(pixels)
        gradient_x[:, 1:] = np.abs(pixels[:, 1:] - pixels[:, :-1])
        gradient_y[1:, :] = np.abs(pixels[1:, :] - pixels[:-1, :])
        edge_energy = gradient_x + gradient_y

        edge_threshold = max(22.0, float(np.percentile(edge_energy, 94)) * 0.70)
        mask = edge_energy >= edge_threshold

        ys, xs = np.where(mask)
        if xs.size < 16 or ys.size < 16:
            return None

        x1 = int(xs.min())
        y1 = int(ys.min())
        x2 = int(xs.max())
        y2 = int(ys.max())
        surface_width = x2 - x1
        surface_height = y2 - y1
        if surface_width <= 0 or surface_height <= 0:
            return None

        width_ratio = surface_width / float(width)
        height_ratio = surface_height / float(height)
        area_ratio = (surface_width * surface_height) / float(max(1, width * height))
        if width_ratio < 0.25 or height_ratio < 0.20 or area_ratio < 0.08:
            return None

        points = list(zip(xs.astype(float), ys.astype(float)))
        corners = self._estimate_quad_corners(points, width, height)

        confidence = min(0.92, max(0.66, 0.62 + area_ratio * 0.32))
        return {
            "corners": [
                round(corners[0][0], 2), round(corners[0][1], 2),
                round(corners[1][0], 2), round(corners[1][1], 2),
                round(corners[2][0], 2), round(corners[2][1], 2),
                round(corners[3][0], 2), round(corners[3][1], 2),
            ],
            "confidence": round(confidence, 3),
            "method": "contour_quadrilateral",
            "source": "image_edges",
        }

    def crop_surface(self, image: Image.Image | None, surface: dict[str, Any] | None) -> SurfaceCrop | None:
        if image is None or surface is None:
            return None

        width, height = image.size
        corners = surface.get("corners")
        if not isinstance(corners, list) or len(corners) < 8 or width <= 0 or height <= 0:
            return None

        points = self._parse_surface_corners(corners)
        if points is None:
            return None

        xs = [point[0] for point in points]
        ys = [point[1] for point in points]
        x1 = self._clamp_int(int(np.floor(min(xs))), 0, width)
        y1 = self._clamp_int(int(np.floor(min(ys))), 0, height)
        x2 = self._clamp_int(int(np.ceil(max(xs))), 0, width)
        y2 = self._clamp_int(int(np.ceil(max(ys))), 0, height)
        if x2 <= x1 or y2 <= y1:
            return None

        output_width = max(
            1,
            round(max(self._distance(points[0], points[1]), self._distance(points[3], points[2]))),
        )
        output_height = max(
            1,
            round(max(self._distance(points[0], points[3]), self._distance(points[1], points[2]))),
        )
        if output_width < 8 or output_height < 8:
            return None

        coefficients = self._perspective_coefficients(
            [(0, 0), (output_width, 0), (output_width, output_height), (0, output_height)],
            points,
        )
        if coefficients is None:
            crop = image.crop((x1, y1, x2, y2))
            points = ((float(x1), float(y1)), (float(x2), float(y1)), (float(x2), float(y2)), (float(x1), float(y2)))
        else:
            crop = image.transform(
                (output_width, output_height),
                Image.Transform.PERSPECTIVE,
                coefficients,
                resample=Image.Resampling.BICUBIC,
            )

        return SurfaceCrop(
            image=crop,
            x_offset=x1,
            y_offset=y1,
            original_width=width,
            original_height=height,
            source_corners=tuple(points),
            perspective_coefficients=coefficients,
        )

    def map_blocks_from_crop(self, blocks: list[dict[str, Any]], crop: SurfaceCrop) -> list[dict[str, Any]]:
        mapped_blocks: list[dict[str, Any]] = []
        for block in blocks:
            mapped = dict(block)
            bbox = block.get("bbox")
            if isinstance(bbox, list) and len(bbox) >= 4:
                try:
                    x1, y1, x2, y2 = [float(value) for value in bbox[:4]]
                    mapped_points = [
                        self._map_crop_point_to_source(crop, x1, y1),
                        self._map_crop_point_to_source(crop, x2, y1),
                        self._map_crop_point_to_source(crop, x2, y2),
                        self._map_crop_point_to_source(crop, x1, y2),
                    ]
                    xs = [point[0] for point in mapped_points]
                    ys = [point[1] for point in mapped_points]
                    mapped["bbox"] = [
                        self._clamp_int(round(min(xs)), 0, crop.original_width),
                        self._clamp_int(round(min(ys)), 0, crop.original_height),
                        self._clamp_int(round(max(xs)), 0, crop.original_width),
                        self._clamp_int(round(max(ys)), 0, crop.original_height),
                    ]
                except (TypeError, ValueError):
                    pass

            mapped_blocks.append(mapped)

        return mapped_blocks

    def estimate_from_ocr_blocks(
        self,
        blocks: list[dict[str, Any]],
        image_width: int,
        image_height: int,
    ) -> dict[str, Any] | None:
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
        confidence = max(0.25, min(0.62, 0.30 + coverage))

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

    def _clamp_float(self, value: float, lower: float, upper: float) -> float:
        return max(lower, min(upper, value))

    def _clamp_int(self, value: int, lower: int, upper: int) -> int:
        return max(lower, min(upper, value))

    def _estimate_quad_corners(
        self,
        points: list[tuple[float, float]],
        image_width: int,
        image_height: int,
    ) -> tuple[tuple[float, float], tuple[float, float], tuple[float, float], tuple[float, float]]:
        top_left = min(points, key=lambda point: point[0] + point[1])
        bottom_right = max(points, key=lambda point: point[0] + point[1])
        top_right = max(points, key=lambda point: point[0] - point[1])
        bottom_left = min(points, key=lambda point: point[0] - point[1])

        pad_x = max(2.0, image_width * 0.006)
        pad_y = max(2.0, image_height * 0.006)
        return (
            self._clamp_point((top_left[0] - pad_x, top_left[1] - pad_y), image_width, image_height),
            self._clamp_point((top_right[0] + pad_x, top_right[1] - pad_y), image_width, image_height),
            self._clamp_point((bottom_right[0] + pad_x, bottom_right[1] + pad_y), image_width, image_height),
            self._clamp_point((bottom_left[0] - pad_x, bottom_left[1] + pad_y), image_width, image_height),
        )

    def _parse_surface_corners(
        self,
        corners: list[Any],
    ) -> tuple[tuple[float, float], tuple[float, float], tuple[float, float], tuple[float, float]] | None:
        points: list[tuple[float, float]] = []
        for i in range(0, 8, 2):
            try:
                points.append((float(corners[i]), float(corners[i + 1])))
            except (TypeError, ValueError):
                return None

        return tuple(points) if len(points) == 4 else None

    def _perspective_coefficients(
        self,
        output_points: list[tuple[float, float]],
        source_points: tuple[tuple[float, float], tuple[float, float], tuple[float, float], tuple[float, float]],
    ) -> tuple[float, float, float, float, float, float, float, float] | None:
        matrix: list[list[float]] = []
        values: list[float] = []
        for (x, y), (source_x, source_y) in zip(output_points, source_points):
            matrix.append([x, y, 1, 0, 0, 0, -source_x * x, -source_x * y])
            matrix.append([0, 0, 0, x, y, 1, -source_y * x, -source_y * y])
            values.extend([source_x, source_y])

        try:
            solved = np.linalg.solve(np.asarray(matrix, dtype=np.float64), np.asarray(values, dtype=np.float64))
        except np.linalg.LinAlgError:
            return None

        return tuple(float(value) for value in solved)

    def _map_crop_point_to_source(self, crop: SurfaceCrop, x: float, y: float) -> tuple[float, float]:
        if crop.perspective_coefficients is not None:
            a, b, c, d, e, f, g, h = crop.perspective_coefficients
            denominator = g * x + h * y + 1.0
            if abs(denominator) > 1e-9:
                source_x = (a * x + b * y + c) / denominator
                source_y = (d * x + e * y + f) / denominator
                return (
                    self._clamp_float(source_x, 0.0, float(crop.original_width)),
                    self._clamp_float(source_y, 0.0, float(crop.original_height)),
                )

        u = self._clamp_float(x / max(1.0, float(crop.image.width)), 0.0, 1.0)
        v = self._clamp_float(y / max(1.0, float(crop.image.height)), 0.0, 1.0)

        top = self._lerp_point(crop.source_corners[0], crop.source_corners[1], u)
        bottom = self._lerp_point(crop.source_corners[3], crop.source_corners[2], u)
        return self._lerp_point(top, bottom, v)

    def _lerp_point(self, a: tuple[float, float], b: tuple[float, float], t: float) -> tuple[float, float]:
        return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)

    def _clamp_point(
        self,
        point: tuple[float, float],
        image_width: int,
        image_height: int,
    ) -> tuple[float, float]:
        return (
            self._clamp_float(point[0], 0.0, float(image_width)),
            self._clamp_float(point[1], 0.0, float(image_height)),
        )

    def _distance(self, a: tuple[float, float], b: tuple[float, float]) -> float:
        return float(np.hypot(a[0] - b[0], a[1] - b[1]))
