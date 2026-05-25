from __future__ import annotations

import re
from dataclasses import dataclass


@dataclass
class MaskedText:
    text: str
    mapping: dict[str, str]
    block_type: str  # text | formula | mixed


class FormulaService:
    """Phát hiện và giữ nguyên công thức.

    Đây là heuristic MVP, không phải parser toán học hoàn chỉnh.

    TODO(MVP) Translation/Formula:
    - Bổ sung regex cho LaTeX, Greek symbols, subscript/superscript.
    - Tách công thức inline tốt hơn thay vì mask cả câu quá rộng.
    - Dùng Math OCR nếu cần: pix2tex/LaTeX-OCR cho công thức phức tạp.
    - Thêm unit tests với slide ML/AI thật.
    """

    MATH_SYMBOLS = set("=+-*/^_∑Σ∫√≤≥≈≠→←∞()[]{}|αβγδθλμσπΩω")

    # Bắt các cụm có dấu = hoặc nhiều ký hiệu toán.
    FORMULA_PATTERNS = [
        re.compile(r"([A-Za-z]\s*=\s*[^.。;,]+)"),
        re.compile(r"([A-Za-z_][A-Za-z0-9_]*\([^)]*\)\s*=\s*[^.。;,]+)"),
        re.compile(r"(\b(?:loss|Loss)\s+is\s+[^.。;,]*[=∑Σ][^.。;,]*)"),
    ]

    def classify(self, text: str) -> str:
        stripped = text.strip()
        if not stripped:
            return "text"

        symbol_count = sum(1 for ch in stripped if ch in self.MATH_SYMBOLS)
        alpha_count = sum(1 for ch in stripped if ch.isalpha())

        if symbol_count >= 2 and alpha_count <= max(8, len(stripped) * 0.45):
            return "formula"
        if symbol_count >= 2:
            return "mixed"
        return "text"

    def mask(self, text: str) -> MaskedText:
        block_type = self.classify(text)
        if block_type == "formula":
            return MaskedText(text="[FORMULA_0]", mapping={"[FORMULA_0]": text}, block_type="formula")

        mapping: dict[str, str] = {}
        masked = text
        counter = 0

        for pattern in self.FORMULA_PATTERNS:
            # Dùng callback để tránh replace lặp sai.
            def repl(match):
                nonlocal counter
                token = f"[FORMULA_{counter}]"
                mapping[token] = match.group(1)
                counter += 1
                return token

            masked = pattern.sub(repl, masked)

        if mapping:
            block_type = "mixed"
        return MaskedText(text=masked, mapping=mapping, block_type=block_type)

    def restore(self, translated_text: str, mapping: dict[str, str]) -> str:
        restored = translated_text
        for token, original in mapping.items():
            restored = restored.replace(token, original)
            match = re.search(r"FORMULA_(\d+)", token)
            if match:
                index = re.escape(match.group(1))
                # Some MT engines mangle placeholders, for example
                # [FORMULA_0] -> [ORMULA 0]. Restore common variants.
                placeholder_pattern = re.compile(
                    rf"\[\s*F?ORMULA[\s_\-]*{index}\s*\]",
                    flags=re.IGNORECASE,
                )
                restored = placeholder_pattern.sub(original, restored)
        return restored
