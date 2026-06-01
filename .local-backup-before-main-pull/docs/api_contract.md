# API Contract

## `GET /health`

Response:

```json
{
  "status": "ok",
  "service": "ar-lecture-translator-backend",
  "mode": "mvp"
}
```

## `POST /pipeline/frame`

Unity gọi endpoint này.

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
      "translated_text": "Học sâu sử dụng mạng nơ-ron.",
      "bbox": [120, 200, 850, 260],
      "confidence": 0.94,
      "type": "text",
      "style": {
        "font_size": 38,
        "background_alpha": 0.68
      }
    }
  ]
}
```

## Quy ước bbox

```text
bbox = [x1, y1, x2, y2]
origin = top-left của ảnh gửi lên backend
unit = pixel
```

Unity sẽ convert sang screen point:

```text
screen_x = bbox_center_x / image_width * Screen.width
screen_y = Screen.height - bbox_center_y / image_height * Screen.height
```

## `POST /ocr`

Endpoint test riêng cho thành viên OCR.

Request:

```json
{
  "image_base64": "...",
  "mock": true
}
```

Response:

```json
{
  "blocks": [
    {
      "id": "b1",
      "text": "Deep learning uses neural networks.",
      "bbox": [205, 180, 1075, 238],
      "confidence": 0.98
    }
  ]
}
```

## `POST /translate`

Endpoint test riêng cho thành viên translation.

Request:

```json
{
  "target_language": "vi",
  "texts": [
    { "id": "b1", "text": "Deep learning uses neural networks." },
    { "id": "b2", "text": "L = -Σ y log(p)" }
  ]
}
```

Response:

```json
{
  "translations": [
    {
      "id": "b1",
      "source_text": "Deep learning uses neural networks.",
      "translated_text": "Học sâu sử dụng mạng nơ-ron.",
      "type": "text"
    }
  ]
}
```
