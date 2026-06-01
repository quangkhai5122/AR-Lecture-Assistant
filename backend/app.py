from __future__ import annotations

import os
from typing import Any

from flask import Flask, jsonify, request
from flask_cors import CORS
from flask_sock import Sock
from werkzeug.exceptions import BadRequest, HTTPException

from services.errors import PipelineError
from services.gemini_service import GeminiService
from services.pipeline_service import PipelineService
from services.speech_service import SpeechToTextService


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
sock = Sock(app)

pipeline_service = PipelineService()
speech_service = SpeechToTextService()
gemini_service = GeminiService()

@sock.route("/speech/stream")
def speech_stream(ws):
    speech_service.stream_websocket(ws)


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
            "speech": os.getenv("SPEECH_PROVIDER", "google"),
            "llm": os.getenv("LLM_PROVIDER", "gemini"),
        },
    })


@app.post("/pipeline")
@app.post("/pipeline/frame")
def pipeline_frame():
    payload = _json_payload()
    _validate_frame_payload(payload)
    result = pipeline_service.process_frame(payload)
    _log_pipeline_latency(result)
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

@app.post("/speech/transcribe")
def speech_transcribe():
    payload = _json_payload()
    _validate_speech_audio_payload(payload)

    result = speech_service.recognize(
        audio_base64=payload.get("audio_base64", ""),
        audio_encoding=payload.get("audio_encoding", "LINEAR16"),
        sample_rate_hz=int(payload.get("sample_rate_hz", 16000)),
        language_code=payload.get("language_code", "en-US"),
        force_mock=bool(payload.get("mock", False)),
        provider=payload.get("speech_provider"),
    )

    return jsonify({
        "transcript": result.transcript,
        "language_code": payload.get("language_code", "en-US"),
        "confidence": result.confidence,
        "provider": {"speech": result.provider},
        "mock_used": result.mock_used,
        "warnings": result.warnings or [],
    })

@app.post("/speech/translate-text")
def speech_translate_text():
    payload = _json_payload()
    _validate_speech_text_payload(payload)

    result = gemini_service.translate_sentence(
        text=payload.get("text", ""),
        source_language=payload.get("source_language", "en-US"),
        target_language=payload.get("target_language", "vi"),
        context=payload.get("context", []),
        force_mock=bool(payload.get("mock", False)),
        provider=payload.get("llm_provider"),
    )

    return jsonify({
        "source_text": payload.get("text", ""),
        "translated_text": result.text,
        "source_language": payload.get("source_language", "en-US"),
        "target_language": payload.get("target_language", "vi"),
        "provider": {"llm": result.provider},
        "model": result.model,
        "mock_used": result.mock_used,
        "warnings": result.warnings,
    })

@app.post("/speech/translate")
def speech_translate_audio():
    payload = _json_payload()
    _validate_speech_audio_payload(payload)

    speech_result = speech_service.recognize(
        audio_base64=payload.get("audio_base64", ""),
        audio_encoding=payload.get("audio_encoding", "LINEAR16"),
        sample_rate_hz=int(payload.get("sample_rate_hz", 16000)),
        language_code=payload.get("language_code", "en-US"),
        force_mock=bool(payload.get("mock", False)),
        provider=payload.get("speech_provider"),
    )

    translation_result = gemini_service.translate_sentence(
        text=speech_result.transcript,
        source_language=payload.get("language_code", "en-US"),
        target_language=payload.get("target_language", "vi"),
        context=payload.get("context", []),
        force_mock=bool(payload.get("mock", False)),
        provider=payload.get("llm_provider"),
    )

    return jsonify({
        "transcript": speech_result.transcript,
        "translated_text": translation_result.text,
        "source_language": payload.get("language_code", "en-US"),
        "target_language": payload.get("target_language", "vi"),
        "confidence": speech_result.confidence,
        "provider": {
            "speech": speech_result.provider,
            "llm": translation_result.provider,
        },
        "model": translation_result.model,
        "mock_used": speech_result.mock_used or translation_result.mock_used,
        "warnings": (speech_result.warnings or []) + translation_result.warnings,
    })

@app.post("/speech/summarize")
def speech_summarize():
    payload = _json_payload()
    _validate_summary_payload(payload)

    result = gemini_service.summarize_notes(
        text=payload.get("text", ""),
        target_language=payload.get("target_language", "vi"),
        force_mock=bool(payload.get("mock", False)),
        provider=payload.get("llm_provider"),
    )

    return jsonify({
        "summary_text": result.text,
        "target_language": payload.get("target_language", "vi"),
        "provider": {"llm": result.provider},
        "model": result.model,
        "mock_used": result.mock_used,
        "warnings": result.warnings,
    })


@app.post("/speech/ask-text")
def speech_ask_text():
    payload = _json_payload()
    _validate_ask_text_payload(payload)

    result = gemini_service.ask_about_text(
        text=payload.get("text", ""),
        target_language=payload.get("target_language", "vi"),
        force_mock=bool(payload.get("mock", False)),
        provider=payload.get("llm_provider"),
    )

    return jsonify({
        "source_text": payload.get("text", ""),
        "answer_text": result.text,
        "target_language": payload.get("target_language", "vi"),
        "provider": {"llm": result.provider},
        "model": result.model,
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

def _validate_speech_audio_payload(payload: dict[str, Any]) -> None:
    if "mock" in payload and not isinstance(payload["mock"], bool):
        raise PipelineError("Field 'mock' must be a boolean.")
    if not bool(payload.get("mock", False)):
        _require_string(payload, "audio_base64")
    elif "audio_base64" in payload and not isinstance(payload["audio_base64"], str):
        raise PipelineError("Field 'audio_base64' must be a string.")

    for key in ("audio_encoding", "language_code", "target_language"):
        if key in payload and not isinstance(payload[key], str):
            raise PipelineError(f"Field '{key}' must be a string.")

    sample_rate = payload.get("sample_rate_hz", 16000)
    if not isinstance(sample_rate, int) or isinstance(sample_rate, bool) or sample_rate <= 0:
        raise PipelineError("Field 'sample_rate_hz' must be a positive integer.")

    _validate_context(payload)
    if payload.get("speech_provider"):
        _require_string(payload, "speech_provider")
    if payload.get("llm_provider"):
        _require_string(payload, "llm_provider")

def _validate_speech_text_payload(payload: dict[str, Any]) -> None:
    _require_string(payload, "text")
    if "mock" in payload and not isinstance(payload["mock"], bool):
        raise PipelineError("Field 'mock' must be a boolean.")
    for key in ("source_language", "target_language"):
        if key in payload and not isinstance(payload[key], str):
            raise PipelineError(f"Field '{key}' must be a string.")
    _validate_context(payload)
    if payload.get("llm_provider"):
        _require_string(payload, "llm_provider")

def _validate_summary_payload(payload: dict[str, Any]) -> None:
    _require_string(payload, "text")
    if "mock" in payload and not isinstance(payload["mock"], bool):
        raise PipelineError("Field 'mock' must be a boolean.")
    if "target_language" in payload and not isinstance(payload["target_language"], str):
        raise PipelineError("Field 'target_language' must be a string.")
    if payload.get("llm_provider"):
        _require_string(payload, "llm_provider")


def _validate_ask_text_payload(payload: dict[str, Any]) -> None:
    _require_string(payload, "text")
    if "mock" in payload and not isinstance(payload["mock"], bool):
        raise PipelineError("Field 'mock' must be a boolean.")
    if "target_language" in payload and not isinstance(payload["target_language"], str):
        raise PipelineError("Field 'target_language' must be a string.")
    if payload.get("llm_provider"):
        _require_string(payload, "llm_provider")


def _validate_context(payload: dict[str, Any]) -> None:
    context = payload.get("context", [])
    if not isinstance(context, list):
        raise PipelineError("Field 'context' must be a list.")
    for index, item in enumerate(context):
        if not isinstance(item, str):
            raise PipelineError(f"Field 'context[{index}]' must be a string.")


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


def _log_pipeline_latency(result: dict[str, Any]) -> None:
    latency = result.get("latency_ms") if isinstance(result, dict) else None
    if not isinstance(latency, dict):
        return

    total_ms = _float_or_none(latency.get("total"))
    if total_ms is None:
        return

    threshold_ms = _float_or_default(os.getenv("PIPELINE_SLOW_MS"), 4000.0)
    log_method = app.logger.warning if total_ms >= threshold_ms else app.logger.info
    prefix = "Slow pipeline" if total_ms >= threshold_ms else "Pipeline latency"
    log_method(
        "%s frame=%s total=%.2fms surface=%.2fms ocr=%.2fms translation=%.2fms",
        prefix,
        result.get("frame_id", ""),
        total_ms,
        _float_or_default(latency.get("surface_detection"), 0.0),
        _float_or_default(latency.get("ocr"), 0.0),
        _float_or_default(latency.get("translation"), 0.0),
    )


def _float_or_none(value: Any) -> float | None:
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _float_or_default(value: Any, default: float) -> float:
    parsed = _float_or_none(value)
    return default if parsed is None else parsed


if __name__ == "__main__":
    host = os.getenv("HOST", "0.0.0.0")
    port = int(os.getenv("PORT", "5000"))
    debug = os.getenv("FLASK_DEBUG", "1") == "1"
    app.run(host=host, port=port, debug=debug)
