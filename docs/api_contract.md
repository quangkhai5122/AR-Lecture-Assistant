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
    "ocr": "tesseract",
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

## `POST /speech/transcribe`

Unity calls this endpoint with microphone PCM16 audio chunks. Backend sends the audio to Google Cloud Speech-to-Text.

Request:

```json
{
  "audio_base64": "...",
  "audio_encoding": "LINEAR16",
  "sample_rate_hz": 16000,
  "language_code": "en-US",
  "mock": false,
  "speech_provider": "google"
}
```

## `WS /speech/stream`

Unity uses this endpoint for low-latency realtime speech. The first WebSocket message must be a JSON text config, then Unity sends binary PCM16 audio frames continuously. Backend keeps a Google Cloud Speech-to-Text `streaming_recognize` session open and returns JSON text messages.

First message:

```json
{
  "audio_encoding": "LINEAR16",
  "sample_rate_hz": 16000,
  "language_code": "en-US",
  "interim_results": true,
  "mock": false,
  "speech_provider": "google"
}
```

Server result message:

```json
{
  "type": "result",
  "transcript": "Today we study neural networks.",
  "is_final": false,
  "stability": 0.82,
  "confidence": 0.0,
  "provider": "google"
}
```

Unity still waits for full sentences before Gemini translation.

Response:

```json
{
  "transcript": "Today we study neural networks.",
  "language_code": "en-US",
  "confidence": 0.92,
  "provider": {"speech": "google"},
  "mock_used": false,
  "warnings": []
}
```

## `POST /speech/translate-text`

Unity calls this only after the transcript sentence gate decides a complete sentence is ready. Backend sends the sentence and recent context to Gemini.

Request:

```json
{
  "text": "Today we study neural networks.",
  "source_language": "en-US",
  "target_language": "vi",
  "context": ["This is a machine learning lecture."],
  "mock": false,
  "llm_provider": "gemini"
}
```

Response:

```json
{
  "source_text": "Today we study neural networks.",
  "translated_text": "Hôm nay chúng ta học về mạng nơ-ron.",
  "source_language": "en-US",
  "target_language": "vi",
  "provider": {"llm": "gemini"},
  "model": "gemini-2.5-flash-lite",
  "mock_used": false,
  "warnings": []
}
```

## `POST /speech/translate`

Convenience endpoint for quick backend tests. It runs audio -> Google Speech-to-Text -> Gemini in one request. The Unity realtime modal uses the two-step flow above so it can wait for full sentences before translation.

## `POST /speech/summarize`

Unity calls this when the user presses `AI summary`. Backend uses Gemini.

Request:

```json
{
  "text": "EN: ...\nVI: ...",
  "target_language": "vi",
  "mock": false,
  "llm_provider": "gemini"
}
```

Response:

```json
{
  "summary_text": "Tóm tắt nội dung chính...",
  "target_language": "vi",
  "provider": {"llm": "gemini"},
  "model": "gemini-2.5-flash-lite",
  "mock_used": false,
  "warnings": []
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
