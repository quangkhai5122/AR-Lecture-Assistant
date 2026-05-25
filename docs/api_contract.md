# API Contract

The backend exposes stable JSON endpoints for Unity and for separate OCR/translation work. `/pipeline` and `/pipeline/frame` are equivalent; keep `/pipeline/frame` for older Unity scripts.

Schema files:

- `contracts/pipeline_request.schema.json`
- `contracts/pipeline_response.schema.json`
- `contracts/ocr_request.schema.json`
- `contracts/ocr_response.schema.json`
- `contracts/translate_request.schema.json`
- `contracts/translate_response.schema.json`

## `GET /health`

Response:

```json
{
  "status": "ok",
  "service": "ar-lecture-translator-backend",
  "mode": "mvp",
  "provider": {
    "ocr": "mock",
    "translation": "mock"
  }
}
```

## `POST /pipeline`

Alias: `POST /pipeline/frame`.

Unity calls this endpoint for the complete flow: OCR -> formula masking -> translation -> AR-ready blocks.

Request:

```json
{
  "frame_id": "frame_001",
  "image_base64": "...",
  "target_language": "vi",
  "mode": "slide_translation",
  "mock": true,
  "image_width": 1280,
  "image_height": 720
}
```

In mock mode, `image_base64` may be empty so Unity can call the mock server before frame capture is implemented. When `mock=false`, `image_base64` is required.

Response:

```json
{
  "frame_id": "frame_001",
  "image_width": 1280,
  "image_height": 720,
  "blocks": [
    {
      "id": "b1",
      "source_text": "Deep learning uses neural networks.",
      "translated_text": "Hoc sau su dung mang no-ron.",
      "bbox": [120, 200, 850, 260],
      "confidence": 0.94,
      "type": "text",
      "style": {
        "font_size": 38,
        "background_alpha": 0.68
      }
    }
  ],
  "provider": {
    "ocr": "mock",
    "translation": "mock"
  },
  "mock_used": true,
  "warnings": [],
  "latency_ms": {
    "ocr": 1.2,
    "translation": 0.4,
    "total": 1.8
  }
}
```

## Bbox Convention

```text
bbox = [x1, y1, x2, y2]
origin = top-left of the image sent to backend
unit = pixel
```

Unity screen conversion:

```text
screen_x = bbox_center_x / image_width * Screen.width
screen_y = Screen.height - bbox_center_y / image_height * Screen.height
```

## `POST /ocr`

Endpoint for OCR module testing.

Request:

```json
{
  "image_base64": "...",
  "mock": true,
  "image_width": 1280,
  "image_height": 720,
  "ocr_provider": "mock"
}
```

Response:

```json
{
  "image_width": 1280,
  "image_height": 720,
  "blocks": [
    {
      "id": "b1",
      "text": "Deep learning uses neural networks.",
      "bbox": [205, 180, 1075, 238],
      "confidence": 0.98
    }
  ],
  "provider": {
    "ocr": "mock"
  },
  "mock_used": true,
  "warnings": []
}
```

## `POST /translate`

Endpoint for translation module testing.

Request:

```json
{
  "target_language": "vi",
  "mock": true,
  "texts": [
    { "id": "b1", "text": "Deep learning uses neural networks." },
    { "id": "b2", "text": "L = -Sigma y log(p)" }
  ],
  "translation_provider": "mock"
}
```

Response:

```json
{
  "translations": [
    {
      "id": "b1",
      "source_text": "Deep learning uses neural networks.",
      "translated_text": "Hoc sau su dung mang no-ron.",
      "type": "text"
    }
  ],
  "provider": {
    "translation": "mock"
  },
  "mock_used": true,
  "warnings": []
}
```
