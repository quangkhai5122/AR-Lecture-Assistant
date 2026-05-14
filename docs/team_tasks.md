# Team Tasks

## Unity AR

Files chính:

```text
client-unity/Assets/Scripts/AR/*
client-unity/Assets/Scripts/UI/ARLectureTranslatorController.cs
```

Cần làm:

- Setup AR scene.
- Build Android.
- Tối ưu anchor/label stability.
- Xử lý plane detection fail.

TODO:

- Mapping bbox vào plane chính xác hơn.
- Label overlap avoidance.
- Tự scale label theo khoảng cách camera.

## OCR

Files chính:

```text
backend/services/ocr_service.py
contracts/sample_ocr_output.json
samples/slides/*
```

Cần làm:

- Thay mock OCR bằng OCR thật.
- Chuẩn hóa output bbox.
- Test trên ảnh slide/bảng thật.

TODO:

- ML Kit / PaddleOCR / Google Vision / Tesseract.
- Gom word thành line/block.
- Tối ưu latency.

## Translation + Formula

Files chính:

```text
backend/services/translation_service.py
backend/services/formula_service.py
```

Cần làm:

- Bổ sung glossary AI/ML.
- Giữ nguyên công thức.
- Dịch batch.

TODO:

- Translation API thật.
- Cache.
- Math OCR/LaTeX nếu cần.

## UI/UX

Files chính:

```text
client-unity/Assets/Scripts/UI/*
docs/demo_script.md
```

Cần làm:

- Scan/Clear/Status/Debug UI.
- Label dễ đọc.
- Loading/error states.

TODO:

- Freeze frame mode.
- Chọn block để hỏi LLM.
- Add Notes.

## Backend + Integration

Files chính:

```text
backend/app.py
backend/services/pipeline_service.py
contracts/*
docs/integration_checklist.md
```

Cần làm:

- Giữ contract ổn định.
- Review PR không phá schema.
- Chạy integration test mỗi ngày.

TODO:

- Docker deploy.
- HTTPS/cloud endpoint.
- Logging request latency.
