from __future__ import annotations

import os
from flask import Flask, jsonify, request
from flask_cors import CORS

from services.pipeline_service import PipelineService

app = Flask(__name__)
CORS(app)

pipeline_service = PipelineService()


@app.get("/health")
def health():
    return jsonify({
        "status": "ok",
        "service": "ar-lecture-translator-backend",
        "mode": "mvp"
    })


@app.post("/pipeline/frame")
def pipeline_frame():
    """Unity gọi endpoint này để OCR + dịch một frame.

    Request JSON:
    {
      "frame_id": "...",
      "image_base64": "...",
      "target_language": "vi",
      "mode": "slide_translation",
      "mock": true
    }
    """
    payload = request.get_json(silent=True) or {}
    result = pipeline_service.process_frame(payload)
    return jsonify(result)


@app.post("/ocr")
def ocr_only():
    """Endpoint phụ cho OCR test độc lập."""
    payload = request.get_json(silent=True) or {}
    blocks = pipeline_service.ocr_service.recognize(
        image_base64=payload.get("image_base64", ""),
        image_width=payload.get("image_width"),
        image_height=payload.get("image_height"),
        force_mock=payload.get("mock", True),
    )
    return jsonify({"blocks": blocks})


@app.post("/translate")
def translate_only():
    """Endpoint phụ cho translation test độc lập."""
    payload = request.get_json(silent=True) or {}
    texts = payload.get("texts", [])
    target_language = payload.get("target_language", "vi")
    translations = []
    for item in texts:
        text = item.get("text", "") if isinstance(item, dict) else str(item)
        block_id = item.get("id", "") if isinstance(item, dict) else ""
        translated, block_type = pipeline_service.translate_preserving_formula(text, target_language)
        translations.append({
            "id": block_id,
            "source_text": text,
            "translated_text": translated,
            "type": block_type,
        })
    return jsonify({"translations": translations})


if __name__ == "__main__":
    host = os.getenv("HOST", "0.0.0.0")
    port = int(os.getenv("PORT", "5000"))
    debug = os.getenv("FLASK_DEBUG", "1") == "1"
    app.run(host=host, port=port, debug=debug)
