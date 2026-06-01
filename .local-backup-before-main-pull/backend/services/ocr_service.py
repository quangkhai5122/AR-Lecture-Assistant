from __future__ import annotations

import base64
import io
import os
from typing import Any

from PIL import Image


class OCRService:
    """OCR layer.

    MVP mặc định trả mock blocks để Unity/backend tích hợp ngay.

    TODO(MVP) OCR:
    - Quyết định engine thật: ML Kit on Android, Google Vision API, PaddleOCR, Tesseract, EasyOCR.
    - Chuẩn hóa output thành bbox [x1, y1, x2, y2] origin top-left.
    - Gom chữ theo line/paragraph thay vì từng word.
    - Thêm confidence và lọc noise.
    - Test với slide nghiêng, bảng trắng, chữ nhỏ, ánh sáng lớp học.
    """

    def recognize(
        self,
        image_base64: str,
        image_width: int | None = None,
        image_height: int | None = None,
        force_mock: bool = True,
    ) -> list[dict[str, Any]]:
        if force_mock or not image_base64:
            return self._mock_blocks(image_width or 1280, image_height or 720)

        use_tesseract = os.getenv("USE_TESSERACT", "0") == "1"
        if use_tesseract:
            try:
                return self._tesseract_blocks(image_base64)
            except Exception as exc:
                # Để demo không chết khi OCR thật lỗi.
                print(f"[OCRService] Tesseract failed, fallback mock: {exc}")
                return self._mock_blocks(image_width or 1280, image_height or 720)

        # TODO(MVP): thay fallback này bằng OCR service thật.
        return self._mock_blocks(image_width or 1280, image_height or 720)

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

    def _tesseract_blocks(self, image_base64: str) -> list[dict[str, Any]]:
        """Optional OCR thật bằng pytesseract.

        Cần:
        - pip install pytesseract pillow
        - cài binary tesseract trên OS
        - đặt USE_TESSERACT=1

        Lưu ý: Tesseract không phải lựa chọn tốt nhất cho real-time mobile AR,
        nhưng đủ để thử backend offline trên ảnh slide.
        """
        import pytesseract  # type: ignore

        cmd = os.getenv("TESSERACT_CMD")
        if cmd:
            pytesseract.pytesseract.tesseract_cmd = cmd

        image_bytes = base64.b64decode(image_base64)
        image = Image.open(io.BytesIO(image_bytes)).convert("RGB")

        data = pytesseract.image_to_data(image, output_type=pytesseract.Output.DICT)

        # Gom các word thành line dựa trên block_num/par_num/line_num.
        lines: dict[tuple[int, int, int], list[int]] = {}
        n = len(data.get("text", []))
        for i in range(n):
            text = data["text"][i].strip()
            if not text:
                continue
            try:
                conf = float(data["conf"][i])
            except Exception:
                conf = -1
            if conf < 30:
                continue
            key = (data["block_num"][i], data["par_num"][i], data["line_num"][i])
            lines.setdefault(key, []).append(i)

        blocks: list[dict[str, Any]] = []
        for idx, (_, indices) in enumerate(lines.items(), start=1):
            words = [data["text"][i].strip() for i in indices if data["text"][i].strip()]
            if not words:
                continue

            x1 = min(data["left"][i] for i in indices)
            y1 = min(data["top"][i] for i in indices)
            x2 = max(data["left"][i] + data["width"][i] for i in indices)
            y2 = max(data["top"][i] + data["height"][i] for i in indices)
            confs = []
            for i in indices:
                try:
                    confs.append(float(data["conf"][i]))
                except Exception:
                    pass
            confidence = max(0.0, min(1.0, (sum(confs) / len(confs) / 100.0) if confs else 0.0))

            blocks.append({
                "id": f"ocr_{idx}",
                "text": " ".join(words),
                "bbox": [x1, y1, x2, y2],
                "confidence": confidence,
            })

        return blocks or self._mock_blocks(image.width, image.height)
