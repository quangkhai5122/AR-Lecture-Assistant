from __future__ import annotations

import os
from typing import Any

from flask import Flask, jsonify, request
from flask_cors import CORS
from werkzeug.exceptions import BadRequest, HTTPException

from services.errors import PipelineError
from services.pipeline_service import PipelineService


def _load_local_env() -> None:
    env_path = os.path.join(os.path.dirname(__file__), ".env")
    if not os.path.exists(env_path):
        return

    with open(env_path, "r", encoding="utf-8") as env_file:
        for raw_line in env_file:
            line = raw_line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue

            key, value = line.split("=", 1)
            key = key.strip()
            value = value.strip().strip('"').strip("'")
            if key and key not in os.environ:
                os.environ[key] = value


_load_local_env()

app = Flask(__name__)
CORS(app)

pipeline_service = PipelineService()


@app.errorhandler(PipelineError)
def handle_pipeline_error(exc: PipelineError):
    return jsonify({
        "error": {
            "code": exc.code,
            "message": exc.message,
        }
    }), exc.status_code


@app.errorhandler(BadRequest)
def handle_bad_request(exc: BadRequest):
    return jsonify({
        "error": {
            "code": "bad_json",
            "message": "Request body must be valid JSON.",
        }
    }), 400


@app.errorhandler(Exception)
def handle_unexpected_error(exc: Exception):
    if isinstance(exc, HTTPException):
        return jsonify({
            "error": {
                "code": exc.name.lower().replace(" ", "_"),
                "message": exc.description,
            }
        }), exc.code or 500

    app.logger.exception("Unhandled backend error")
    return jsonify({
        "error": {
            "code": "internal_error",
            "message": str(exc),
        }
    }), 500


@app.get("/health")
def health():
    return jsonify({
        "status": "ok",
        "service": "ar-lecture-translator-backend",
        "mode": "mvp",
        "provider": {
            "ocr": os.getenv("OCR_PROVIDER", "tesseract"),
            "translation": os.getenv("TRANSLATION_PROVIDER", "mock"),
        },
    })


@app.post("/pipeline")
@app.post("/pipeline/frame")
def pipeline_frame():
    payload = _json_payload()
    _validate_frame_payload(payload)
    result = pipeline_service.process_frame(payload)
    return jsonify(result)


@app.post("/ocr")
def ocr_only():
    payload = _json_payload()
    _validate_ocr_payload(payload)
    force_mock = bool(payload.get("mock", True))
    result = pipeline_service.ocr_service.recognize(
        image_base64=payload.get("image_base64", ""),
        image_width=payload.get("image_width"),
        image_height=payload.get("image_height"),
        force_mock=force_mock,
        provider=payload.get("ocr_provider"),
    )
    return jsonify({
        "image_width": result.image_width,
        "image_height": result.image_height,
        "blocks": result.blocks,
        "provider": {"ocr": result.provider},
        "mock_used": result.mock_used,
        "warnings": result.warnings,
    })


@app.post("/translate")
def translate_only():
    payload = _json_payload()
    _validate_translate_payload(payload)

    texts = payload.get("texts", [])
    force_mock = bool(payload.get("mock", True))
    target_language = payload.get("target_language", "vi")
    source_items = [
        item if isinstance(item, dict) else {"id": "", "text": str(item)}
        for item in texts
    ]

    translated_blocks, result = pipeline_service.translate_blocks_preserving_formula(
        [
            {
                "id": item.get("id", ""),
                "text": item.get("text", ""),
                "bbox": [0, 0, 0, 0],
                "confidence": 1.0,
            }
            for item in source_items
        ],
        target_language=target_language,
        force_mock=force_mock,
        provider=payload.get("translation_provider"),
    )

    return jsonify({
        "translations": [
            {
                "id": block["id"],
                "source_text": block["source_text"],
                "translated_text": block["translated_text"],
                "type": block["type"],
            }
            for block in translated_blocks
        ],
        "provider": {"translation": result.provider},
        "mock_used": result.mock_used,
        "warnings": result.warnings,
    })


def _json_payload() -> dict[str, Any]:
    payload = request.get_json(silent=False)
    if not isinstance(payload, dict):
        raise PipelineError("Request body must be a JSON object.")
    return payload


def _validate_frame_payload(payload: dict[str, Any]) -> None:
    _require_string(payload, "frame_id")
    _require_string(payload, "target_language")
    _validate_common_payload(payload)
    _validate_image_payload(payload, required=not bool(payload.get("mock", True)))


def _validate_ocr_payload(payload: dict[str, Any]) -> None:
    _validate_common_payload(payload)
    _validate_image_payload(payload, required=not bool(payload.get("mock", True)))


def _validate_translate_payload(payload: dict[str, Any]) -> None:
    _require_string(payload, "target_language")
    if "mock" in payload and not isinstance(payload["mock"], bool):
        raise PipelineError("Field 'mock' must be a boolean.")
    if payload.get("translation_provider"):
        _require_string(payload, "translation_provider")
    texts = payload.get("texts")
    if not isinstance(texts, list):
        raise PipelineError("Field 'texts' must be a list.")
    for index, item in enumerate(texts):
        if isinstance(item, str):
            continue
        if not isinstance(item, dict):
            raise PipelineError(f"Field 'texts[{index}]' must be a string or object.")
        if not isinstance(item.get("text"), str):
            raise PipelineError(f"Field 'texts[{index}].text' must be a string.")


def _validate_common_payload(payload: dict[str, Any]) -> None:
    if "mock" in payload and not isinstance(payload["mock"], bool):
        raise PipelineError("Field 'mock' must be a boolean.")
    for key in ("image_width", "image_height"):
        if key in payload and (
            not isinstance(payload[key], int)
            or isinstance(payload[key], bool)
            or payload[key] <= 0
        ):
            raise PipelineError(f"Field '{key}' must be a positive integer.")
    if payload.get("ocr_provider"):
        _require_string(payload, "ocr_provider")
    if payload.get("translation_provider"):
        _require_string(payload, "translation_provider")


def _require_string(payload: dict[str, Any], key: str) -> None:
    if not isinstance(payload.get(key), str) or not payload[key].strip():
        raise PipelineError(f"Field '{key}' is required and must be a non-empty string.")


def _validate_image_payload(payload: dict[str, Any], required: bool) -> None:
    if required:
        _require_string(payload, "image_base64")
        return

    if "image_base64" in payload and not isinstance(payload["image_base64"], str):
        raise PipelineError("Field 'image_base64' must be a string.")


if __name__ == "__main__":
    host = os.getenv("HOST", "0.0.0.0")
    port = int(os.getenv("PORT", "5000"))
    debug = os.getenv("FLASK_DEBUG", "1") == "1"
    app.run(host=host, port=port, debug=debug)
