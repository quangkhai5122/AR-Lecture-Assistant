from services.pipeline_service import PipelineService


def test_pipeline_mock_returns_blocks():
    service = PipelineService()
    result = service.process_frame({"frame_id": "test", "mock": True})
    assert result["frame_id"] == "test"
    assert result["blocks"]
    assert "translated_text" in result["blocks"][0]


def test_formula_preservation():
    service = PipelineService()
    translated, block_type = service.translate_preserving_formula(
        "The loss is L = -Σ y log(p).",
        "vi",
    )
    assert "L = -Σ y log(p)" in translated
    assert block_type in {"mixed", "formula"}
