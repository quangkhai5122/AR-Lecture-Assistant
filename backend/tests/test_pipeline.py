from __future__ import annotations

import base64
import builtins
import importlib.util
import io
import json
import shutil
from pathlib import Path

import pytest
import requests
from jsonschema import Draft202012Validator
from PIL import Image, ImageDraw

from app import app
from services.document_surface_service import DocumentSurfaceService
from services.errors import PipelineError
from services.ocr_service import OCRResult
from services.ocr_service import OCRService
from services.pipeline_service import PipelineService
from services.translation_service import TranslationService


def _image_base64(width: int = 64, height: int = 32) -> str:
    image = Image.new("RGB", (width, height), color=(255, 255, 255))
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    return base64.b64encode(buffer.getvalue()).decode("utf-8")


def _surface_image_base64(width: int = 320, height: int = 220) -> str:
    image = Image.new("RGB", (width, height), color=(42, 46, 52))
    draw = ImageDraw.Draw(image)
    draw.polygon(
        [(44, 32), (282, 24), (296, 190), (34, 198)],
        fill=(246, 246, 242),
        outline=(18, 18, 18),
    )
    draw.text((78, 76), "Shallow neural networks", fill=(30, 30, 30))
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    return base64.b64encode(buffer.getvalue()).decode("utf-8")


def _contract_schema(name: str) -> dict:
    path = Path(__file__).resolve().parents[2] / "contracts" / name
    return json.loads(path.read_text(encoding="utf-8"))


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
    assert result["document_surface"]["method"] == "ocr_bbox_union"
    assert len(result["document_surface"]["corners"]) == 8
    assert "surface_detection" in result["latency_ms"]


def test_pipeline_response_matches_contract_schema():
    service = PipelineService()
    result = service.process_frame({
        "frame_id": "contract_test",
        "image_base64": _image_base64(),
        "target_language": "vi",
        "mock": True,
    })
    validator = Draft202012Validator(_contract_schema("pipeline_response.schema.json"))

    validator.validate(result)


def test_sample_pipeline_output_matches_contract_schema():
    sample_path = Path(__file__).resolve().parents[2] / "contracts" / "sample_pipeline_output.json"
    sample = json.loads(sample_path.read_text(encoding="utf-8"))
    validator = Draft202012Validator(_contract_schema("pipeline_response.schema.json"))

    validator.validate(sample)


def test_mock_pipeline_latency_budget_under_one_second():
    service = PipelineService()
    result = service.process_frame({
        "frame_id": "latency_budget",
        "image_base64": _image_base64(),
        "target_language": "vi",
        "mock": True,
    })

    assert result["latency_ms"]["total"] < 1000


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


def test_pipeline_endpoint_alias_accepts_mock_without_image():
    client = app.test_client()
    response = client.post("/pipeline", json={
        "frame_id": "unity_day_one",
        "target_language": "vi",
        "mock": True,
        "image_width": 1280,
        "image_height": 720,
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["frame_id"] == "unity_day_one"
    assert data["image_width"] == 1280
    assert data["image_height"] == 720
    assert data["provider"] == {"ocr": "mock", "translation": "mock"}
    assert data["mock_used"] is True
    assert data["blocks"]


def test_pipeline_endpoint_logs_slow_latency(monkeypatch):
    calls = []

    def fake_warning(message, *args, **kwargs):
        calls.append((message, args, kwargs))

    monkeypatch.setenv("PIPELINE_SLOW_MS", "0")
    monkeypatch.setattr(app.logger, "warning", fake_warning)

    client = app.test_client()
    response = client.post("/pipeline/frame", json={
        "frame_id": "latency_log",
        "target_language": "vi",
        "mock": True,
        "image_width": 1280,
        "image_height": 720,
    })

    assert response.status_code == 200
    assert calls
    assert calls[0][1][0] == "Slow pipeline"
    assert calls[0][1][1] == "latency_log"


def test_pipeline_frame_real_mode_requires_image():
    client = app.test_client()
    response = client.post("/pipeline/frame", json={
        "frame_id": "real_missing_image",
        "target_language": "vi",
        "mock": False,
        "ocr_provider": "mock",
        "translation_provider": "mock",
    })
    data = response.get_json()
    assert response.status_code == 400
    assert data["error"]["code"] == "pipeline_error"
    assert "image_base64" in data["error"]["message"]


def test_ocr_endpoint_accepts_mock_without_image():
    client = app.test_client()
    response = client.post("/ocr", json={
        "mock": True,
        "image_width": 640,
        "image_height": 360,
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["image_width"] == 640
    assert data["image_height"] == 360
    assert data["provider"] == {"ocr": "mock"}
    assert data["mock_used"] is True
    assert data["blocks"]


def test_ocr_endpoint_accepts_empty_provider_as_default():
    client = app.test_client()
    response = client.post("/ocr", json={
        "mock": True,
        "image_width": 640,
        "image_height": 360,
        "ocr_provider": "",
    })
    assert response.status_code == 200
    assert response.get_json()["provider"] == {"ocr": "mock"}


@pytest.mark.skipif(shutil.which("tesseract") is None, reason="tesseract binary is not installed")
def test_tesseract_ocr_on_sample_slide(monkeypatch):
    pytest.importorskip("pytesseract")

    image_path = Path(__file__).resolve().parents[2] / "samples" / "slides" / "slide_01.png"
    monkeypatch.setenv("OCR_PROVIDER", "tesseract")
    monkeypatch.setenv("OCR_MIN_CONFIDENCE", "0.2")

    client = app.test_client()
    with Image.open(image_path) as image:
        width, height = image.size

    response = client.post("/ocr", json={
        "mock": False,
        "image_base64": base64.b64encode(image_path.read_bytes()).decode("utf-8"),
        "image_width": width,
        "image_height": height,
        "ocr_provider": "tesseract",
    })
    data = response.get_json()

    assert response.status_code == 200
    assert data["provider"] == {"ocr": "tesseract"}
    assert data["mock_used"] is False
    assert data["blocks"]
    assert all(block["bbox"][2] > block["bbox"][0] for block in data["blocks"])
    assert all(block["bbox"][3] > block["bbox"][1] for block in data["blocks"])
    assert all(0 <= block["confidence"] <= 1 for block in data["blocks"])


@pytest.mark.skipif(shutil.which("tesseract") is None, reason="tesseract binary is not installed")
def test_pipeline_uses_real_tesseract_ocr_with_mock_translation(monkeypatch):
    pytest.importorskip("pytesseract")

    image_path = Path(__file__).resolve().parents[2] / "samples" / "slides" / "slide_01.png"
    monkeypatch.setenv("OCR_PROVIDER", "tesseract")
    monkeypatch.setenv("OCR_MIN_CONFIDENCE", "0.2")

    client = app.test_client()
    with Image.open(image_path) as image:
        width, height = image.size

    response = client.post("/pipeline/frame", json={
        "frame_id": "slide_01_real_ocr",
        "mock": False,
        "target_language": "vi",
        "image_base64": base64.b64encode(image_path.read_bytes()).decode("utf-8"),
        "image_width": width,
        "image_height": height,
        "ocr_provider": "tesseract",
        "translation_provider": "mock",
    })
    data = response.get_json()

    assert response.status_code == 200
    assert data["provider"] == {"ocr": "tesseract", "translation": "mock"}
    assert data["blocks"]
    assert data["blocks"][0]["source_text"]
    assert data["blocks"][0]["translated_text"]
    assert data["document_surface"]["corners"]
    assert all("bbox" in block and len(block["bbox"]) == 4 for block in data["blocks"])


def test_document_surface_estimation_from_ocr_boxes():
    service = PipelineService()
    surface = service.estimate_document_surface(
        [
            {"bbox": [100, 120, 500, 160]},
            {"bbox": [140, 300, 620, 350]},
        ],
        image_width=800,
        image_height=600,
    )

    assert surface is not None
    assert surface["method"] == "ocr_bbox_union"
    assert surface["corners"][0] < 100
    assert surface["corners"][1] < 120
    assert surface["corners"][4] > 620
    assert surface["corners"][5] > 350


def test_document_surface_detects_quadrilateral():
    service = DocumentSurfaceService()
    image = service.decode_image(_surface_image_base64())
    surface = service.detect(image, [], 320, 220)

    assert surface is not None
    assert surface["method"] == "contour_quadrilateral"
    assert surface["source"] == "image_edges"
    assert len(surface["corners"]) == 8
    assert 0 <= surface["confidence"] <= 1
    assert surface["corners"][0] <= 48
    assert surface["corners"][4] >= 286


def test_document_surface_falls_back_to_ocr_union():
    service = DocumentSurfaceService()
    blank = Image.new("RGB", (320, 220), color=(255, 255, 255))
    surface = service.detect(
        blank,
        [{"bbox": [90, 70, 210, 92]}, {"bbox": [94, 118, 240, 142]}],
        320,
        220,
    )

    assert surface is not None
    assert surface["method"] == "ocr_bbox_union"
    assert surface["source"] == "ocr_blocks"
    assert 0 <= surface["confidence"] <= 1


def test_document_surface_returns_none_for_blank_image():
    service = DocumentSurfaceService()
    blank = Image.new("RGB", (320, 220), color=(255, 255, 255))

    assert service.detect(blank, [], 320, 220) is None


def test_document_surface_crop_maps_ocr_boxes_to_original_image():
    service = DocumentSurfaceService()
    image = service.decode_image(_surface_image_base64())
    surface = service.detect_from_image(image)
    crop = service.crop_surface(image, surface)

    assert crop is not None
    assert crop.image.width < image.width
    assert crop.image.height < image.height

    mapped = service.map_blocks_from_crop(
        [{"id": "ocr_1", "text": "Inside", "bbox": [10, 12, 80, 30], "confidence": 0.9}],
        crop,
    )

    assert mapped[0]["bbox"][0] > crop.x_offset
    assert mapped[0]["bbox"][1] > crop.y_offset
    assert mapped[0]["bbox"][2] > mapped[0]["bbox"][0]
    assert mapped[0]["bbox"][3] > mapped[0]["bbox"][1]
    assert mapped[0]["bbox"][2] <= image.width
    assert mapped[0]["bbox"][3] <= image.height


def test_document_surface_crop_uses_quadrilateral_warp_mapping():
    service = DocumentSurfaceService()
    image = Image.new("RGB", (320, 220), color=(35, 35, 35))
    surface = {
        "corners": [40, 20, 250, 40, 280, 180, 30, 170],
        "confidence": 0.9,
        "method": "contour_quadrilateral",
        "source": "test",
    }
    crop = service.crop_surface(image, surface)

    assert crop is not None
    assert crop.source_corners[0] == (40.0, 20.0)
    assert crop.source_corners[1] == (250.0, 40.0)

    mapped = service.map_blocks_from_crop(
        [{"id": "ocr_1", "text": "Skewed", "bbox": [20, 15, 80, 35], "confidence": 0.9}],
        crop,
    )

    assert mapped[0]["bbox"] != [crop.x_offset + 20, crop.y_offset + 15, crop.x_offset + 80, crop.y_offset + 35]
    assert mapped[0]["bbox"][2] > mapped[0]["bbox"][0]
    assert mapped[0]["bbox"][3] > mapped[0]["bbox"][1]


def test_document_surface_detects_four_of_five_sample_slides():
    service = DocumentSurfaceService()
    sample_dir = Path(__file__).resolve().parents[2] / "samples" / "slides"
    sample_paths = sorted(sample_dir.glob("slide_*.png"))

    assert len(sample_paths) >= 5

    detected = 0
    for sample_path in sample_paths[:5]:
        with Image.open(sample_path) as image:
            surface = service.detect_from_image(image.convert("RGB"))
        if surface is not None:
            detected += 1

    assert detected >= 4


def test_pipeline_runs_real_ocr_on_surface_crop(monkeypatch):
    service = PipelineService()
    seen_sizes: list[tuple[int, int]] = []

    def fake_recognize(
        image_base64,
        image_width=None,
        image_height=None,
        force_mock=True,
        provider=None,
    ):
        image = service.document_surface_service.decode_image(image_base64)
        seen_sizes.append(image.size)
        return OCRResult(
            blocks=[{"id": "ocr_1", "text": "Inside surface", "bbox": [8, 10, 120, 32], "confidence": 0.9}],
            image_width=image.width,
            image_height=image.height,
            provider="fake",
            mock_used=False,
            warnings=[],
        )

    monkeypatch.setattr(service.ocr_service, "recognize", fake_recognize)

    result = service.process_frame({
        "frame_id": "surface_crop",
        "image_base64": _surface_image_base64(),
        "mock": False,
        "ocr_provider": "tesseract",
        "translation_provider": "mock",
    })

    assert seen_sizes
    assert seen_sizes[0][0] < 320
    assert seen_sizes[0][1] < 220
    assert result["image_width"] == 320
    assert result["image_height"] == 220
    assert result["document_surface"]["method"] == "contour_quadrilateral"
    assert result["blocks"][0]["bbox"][0] > 8
    assert any("surface crop" in warning for warning in result["warnings"])


def test_translate_endpoint_mock_keeps_ids_and_formula_type():
    client = app.test_client()
    response = client.post("/translate", json={
        "target_language": "vi",
        "mock": True,
        "texts": [
            {"id": "b1", "text": "Deep learning uses neural networks."},
            {"id": "f1", "text": "L = -Î£ y log(p)"},
        ],
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["provider"] == {"translation": "mock"}
    assert data["mock_used"] is True
    assert [item["id"] for item in data["translations"]] == ["b1", "f1"]
    assert data["translations"][0]["translated_text"]
    assert data["translations"][1]["type"] == "formula"
    assert data["translations"][1]["translated_text"] == "L = -Î£ y log(p)"


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


def test_paddleocr_normalization_maps_upscaled_boxes_to_original_image():
    service = OCRService()
    raw = [
        {
            "rec_texts": ["Upscaled text"],
            "rec_scores": [0.92],
            "rec_polys": [
                [[20, 10], [100, 10], [100, 30], [20, 30]],
            ],
        }
    ]

    blocks = service.normalize_paddle_output(
        raw,
        image_width=100,
        image_height=50,
        min_confidence=0.45,
        scale_x=2.0,
        scale_y=2.0,
    )

    assert blocks == [
        {
            "id": "ocr_1",
            "text": "Upscaled text",
            "bbox": [10, 5, 50, 15],
            "confidence": 0.92,
        }
    ]


def test_ocr_preprocess_upscales_small_images_with_cap(monkeypatch):
    service = OCRService()
    monkeypatch.setenv("OCR_UPSCALE_LONG_SIDE", "2200")
    monkeypatch.setenv("OCR_MAX_UPSCALE", "2")
    monkeypatch.setenv("OCR_CONTRAST", "1")
    monkeypatch.setenv("OCR_SHARPNESS", "1")
    monkeypatch.setenv("OCR_UNSHARP_MASK", "0")

    image = Image.new("RGB", (800, 400), color=(255, 255, 255))
    prepared, scale_x, scale_y = service._prepare_ocr_image(image)

    assert prepared.size == (1600, 800)
    assert scale_x == 2
    assert scale_y == 2


def test_paddle_detection_parameters_default_to_small_text_mode(monkeypatch):
    monkeypatch.delenv("PADDLEOCR_DET_LIMIT_SIDE_LEN", raising=False)
    monkeypatch.delenv("PADDLEOCR_DET_LIMIT_TYPE", raising=False)
    monkeypatch.delenv("PADDLEOCR_DET_BOX_THRESH", raising=False)

    service = OCRService()

    assert service._paddle_detection_parameters() == {
        "text_det_limit_type": "min",
        "text_det_limit_side_len": 1536,
        "text_det_box_thresh": 0.30,
    }
    assert service._paddle_detection_parameters(legacy=True) == {
        "det_limit_type": "min",
        "det_limit_side_len": 1536,
        "det_db_box_thresh": 0.30,
    }

def test_ocr_postprocess_merges_same_line_blocks(monkeypatch):
    monkeypatch.delenv("OCR_MERGE_TEXT_LINES", raising=False)
    service = OCRService()

    blocks = service._postprocess_blocks(
        [
            {"id": "a", "text": "Deep learning", "bbox": [10, 20, 110, 42], "confidence": 0.9},
            {"id": "b", "text": "uses neural networks.", "bbox": [118, 21, 260, 43], "confidence": 0.8},
            {"id": "c", "text": "Gradient descent", "bbox": [10, 80, 150, 102], "confidence": 0.95},
        ],
        image_width=400,
        image_height=200,
    )

    assert blocks == [
        {
            "id": "ocr_1",
            "text": "Deep learning uses neural networks.",
            "bbox": [10, 20, 260, 43],
            "confidence": 0.85,
        },
        {
            "id": "ocr_2",
            "text": "Gradient descent",
            "bbox": [10, 80, 150, 102],
            "confidence": 0.95,
        },
    ]


def test_paddleocr_unavailable_falls_back_to_tesseract(monkeypatch):
    service = OCRService()

    def fail_paddle(image, width, height):
        raise PipelineError(
            "PaddleOCR is unavailable",
            status_code=503,
            code="ocr_provider_not_available",
        )

    def fake_tesseract(image, width, height):
        return [
            {
                "id": "ocr_1",
                "text": "Fallback text",
                "bbox": [1, 2, 30, 12],
                "confidence": 0.8,
            }
        ]

    monkeypatch.setattr(service, "_paddleocr_blocks", fail_paddle)
    monkeypatch.setattr(service, "_tesseract_blocks", fake_tesseract)
    monkeypatch.delenv("OCR_ENABLE_PROVIDER_FALLBACK", raising=False)
    monkeypatch.delenv("OCR_FALLBACK_PROVIDER", raising=False)

    result = service.recognize(
        image_base64=_image_base64(width=80, height=40),
        force_mock=False,
        provider="paddleocr",
    )

    assert result.provider == "tesseract"
    assert result.blocks[0]["text"] == "Fallback text"
    assert any("falling back to 'tesseract'" in warning for warning in result.warnings)


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

def test_speech_transcribe_endpoint_mock_returns_transcript():
    client = app.test_client()
    response = client.post("/speech/transcribe", json={
        "mock": True,
        "language_code": "en-US",
        "sample_rate_hz": 16000,
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["transcript"]
    assert data["provider"] == {"speech": "mock"}
    assert data["mock_used"] is True

def test_speech_translate_text_endpoint_mock_returns_translation():
    client = app.test_client()
    response = client.post("/speech/translate-text", json={
        "mock": True,
        "text": "Today we will learn about neural networks.",
        "source_language": "en-US",
        "target_language": "vi",
        "context": ["We are studying machine learning."],
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["source_text"] == "Today we will learn about neural networks."
    assert data["translated_text"]
    assert data["provider"] == {"llm": "mock"}
    assert data["mock_used"] is True

def test_speech_translate_audio_endpoint_mock_runs_full_flow():
    client = app.test_client()
    response = client.post("/speech/translate", json={
        "mock": True,
        "language_code": "en-US",
        "target_language": "vi",
        "context": [],
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["transcript"]
    assert data["translated_text"]
    assert data["provider"] == {"speech": "mock", "llm": "mock"}
    assert data["mock_used"] is True

def test_speech_summarize_endpoint_mock_returns_summary():
    client = app.test_client()
    response = client.post("/speech/summarize", json={
        "mock": True,
        "text": "EN: Today we study neural networks.\nVI: Hôm nay chúng ta học mạng nơ-ron.",
        "target_language": "vi",
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["summary_text"]
    assert data["provider"] == {"llm": "mock"}
    assert data["mock_used"] is True

def test_speech_ask_text_endpoint_mock_returns_answer():
    client = app.test_client()
    response = client.post("/speech/ask-text", json={
        "mock": True,
        "text": "Mang nong so voi mang sau",
        "target_language": "vi",
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["source_text"] == "Mang nong so voi mang sau"
    assert data["answer_text"]
    assert data["provider"] == {"llm": "mock"}
    assert data["mock_used"] is True

def test_speech_translate_text_gemini_provider_uses_context(monkeypatch):
    calls = []

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {
                "candidates": [
                    {
                        "content": {
                            "parts": [{"text": "Hôm nay chúng ta học mạng nơ-ron."}]
                        }
                    }
                ]
            }

    def fake_post(url, params, json, timeout):
        calls.append((url, params, json, timeout))
        return FakeResponse()

    monkeypatch.setenv("GEMINI_API_KEY", "test-key")
    monkeypatch.setattr("services.gemini_service.requests.post", fake_post)

    client = app.test_client()
    response = client.post("/speech/translate-text", json={
        "mock": False,
        "text": "Today we study neural networks.",
        "source_language": "en-US",
        "target_language": "vi",
        "context": ["This is a machine learning lecture."],
        "llm_provider": "gemini",
    })
    data = response.get_json()
    assert response.status_code == 200
    assert data["translated_text"] == "Hôm nay chúng ta học mạng nơ-ron."
    assert data["provider"] == {"llm": "gemini"}
    assert calls[0][1] == {"key": "test-key"}
    assert "This is a machine learning lecture." in calls[0][2]["contents"][0]["parts"][0]["text"]
