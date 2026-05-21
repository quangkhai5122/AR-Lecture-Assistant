from __future__ import annotations

import base64
import io

import pytest
from PIL import Image

from app import app
from services.ocr_service import OCRService
from services.pipeline_service import PipelineService
from services.translation_service import TranslationService


def _image_base64(width: int = 64, height: int = 32) -> str:
    image = Image.new("RGB", (width, height), color=(255, 255, 255))
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    return base64.b64encode(buffer.getvalue()).decode("utf-8")


def test_pipeline_mock_returns_blocks():
    service = PipelineService()
    result = service.process_frame({
        "frame_id": "test",
        "image_base64": _image_base64(),
        "target_language": "vi",
        "mock": True,
    })
    assert result["frame_id"] == "test"
    assert result["blocks"]
    assert result["mock_used"] is True
    assert result["provider"]["ocr"] == "mock"
    assert "translated_text" in result["blocks"][0]


def test_decoded_image_dimensions_are_used_even_if_hints_differ():
    service = PipelineService()
    result = service.process_frame({
        "frame_id": "test",
        "image_base64": _image_base64(width=80, height=40),
        "target_language": "vi",
        "mock": True,
        "image_width": 1280,
        "image_height": 720,
    })
    assert result["image_width"] == 80
    assert result["image_height"] == 40
    assert any("image_width hint" in warning for warning in result["warnings"])


def test_real_mode_without_libretranslate_url_fails(monkeypatch):
    monkeypatch.delenv("LIBRETRANSLATE_URL", raising=False)
    service = PipelineService()
    with pytest.raises(Exception) as exc_info:
        service.process_frame({
            "frame_id": "test",
            "image_base64": _image_base64(),
            "target_language": "vi",
            "mock": False,
            "ocr_provider": "mock",
            "translation_provider": "libretranslate",
        })
    assert "LIBRETRANSLATE_URL" in str(exc_info.value)


def test_formula_preservation():
    service = PipelineService()
    translated, block_type = service.translate_preserving_formula(
        "The loss is L = -Σ y log(p).",
        "vi",
        force_mock=True,
    )
    assert "L = -Σ y log(p)" in translated
    assert block_type in {"mixed", "formula"}


def test_formula_restore_handles_mangled_placeholder():
    service = PipelineService()
    restored = service.formula_service.restore(
        "Mất mát là [ORMULA 0].",
        {"[FORMULA_0]": "L = -Σ y log(p)"},
    )
    assert restored == "Mất mát là L = -Σ y log(p)."


def test_formula_only_block_skips_translation_provider(monkeypatch):
    monkeypatch.delenv("LIBRETRANSLATE_URL", raising=False)
    service = PipelineService()
    blocks, result = service.translate_blocks_preserving_formula(
        [{"id": "f1", "text": "L = -Σ y log(p)", "bbox": [0, 0, 10, 10], "confidence": 1}],
        target_language="vi",
        force_mock=False,
        provider="libretranslate",
    )
    assert blocks[0]["translated_text"] == "L = -Σ y log(p)"
    assert blocks[0]["type"] == "formula"
    assert result.translations == []


def test_paddleocr_normalization_clamps_and_filters():
    service = OCRService()
    raw = [
        {
            "rec_texts": ["Visible text", "low confidence"],
            "rec_scores": [0.91, 0.2],
            "rec_polys": [
                [[-5, 3], [70, 3], [70, 20], [-5, 20]],
                [[1, 1], [10, 1], [10, 5], [1, 5]],
            ],
        }
    ]
    blocks = service.normalize_paddle_output(raw, image_width=64, image_height=32, min_confidence=0.45)
    assert blocks == [
        {
            "id": "ocr_1",
            "text": "Visible text",
            "bbox": [0, 3, 64, 20],
            "confidence": 0.91,
        }
    ]


def test_translation_cache_avoids_second_request(monkeypatch):
    calls = []

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {"translatedText": ["Xin chào"]}

    def fake_post(url, json, timeout):
        calls.append((url, json, timeout))
        return FakeResponse()

    monkeypatch.setenv("LIBRETRANSLATE_URL", "http://translate.local/translate")
    monkeypatch.setattr("services.translation_service.requests.post", fake_post)

    service = TranslationService()
    first = service.translate_batch(["Hello"], target_language="vi", force_mock=False, provider="libretranslate")
    second = service.translate_batch(["Hello"], target_language="vi", force_mock=False, provider="libretranslate")

    assert first.translations == ["Xin chào"]
    assert second.translations == ["Xin chào"]
    assert len(calls) == 1
    assert second.cache_hits == 1


def test_google_translation_provider_uses_api_key_and_cache(monkeypatch):
    calls = []

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {
                "data": {
                    "translations": [
                        {"translatedText": "Học máy"},
                        {"translatedText": "Học sâu"},
                    ]
                }
            }

    def fake_post(url, params, json, timeout):
        calls.append((url, params, json, timeout))
        return FakeResponse()

    monkeypatch.setenv("GOOGLE_TRANSLATE_API_KEY", "test-key")
    monkeypatch.setattr("services.translation_service.requests.post", fake_post)

    service = TranslationService()
    first = service.translate_batch(
        ["Machine learning", "Deep learning"],
        target_language="vi",
        force_mock=False,
        provider="google",
    )
    second = service.translate_batch(
        ["Machine learning", "Deep learning"],
        target_language="vi",
        force_mock=False,
        provider="google",
    )

    assert first.translations == ["Học máy", "Học sâu"]
    assert second.translations == ["Học máy", "Học sâu"]
    assert len(calls) == 1
    assert calls[0][1] == {"key": "test-key"}
    assert calls[0][2]["q"] == ["Machine learning", "Deep learning"]
    assert calls[0][2]["source"] == "en"
    assert second.cache_hits == 2


def test_google_translation_requires_api_key(monkeypatch):
    monkeypatch.delenv("GOOGLE_TRANSLATE_API_KEY", raising=False)
    service = TranslationService()
    with pytest.raises(Exception) as exc_info:
        service.translate_batch(["Hello"], target_language="vi", force_mock=False, provider="google")
    assert "GOOGLE_TRANSLATE_API_KEY" in str(exc_info.value)


def test_invalid_pipeline_request_returns_400():
    client = app.test_client()
    response = client.post("/pipeline/frame", json={"frame_id": "missing-fields"})
    assert response.status_code == 400
    assert response.get_json()["error"]["code"] == "pipeline_error"


def test_translate_endpoint_validates_texts_shape():
    client = app.test_client()
    response = client.post("/translate", json={"target_language": "vi", "texts": {"bad": "shape"}})
    assert response.status_code == 400
