# Backend: AR Lecture Assistant

Backend này nhận ảnh hoặc audio từ Unity client, sau đó xử lý OCR, dịch, speech-to-text và các tác vụ Gemini.

Nếu bạn cần hướng dẫn đầy đủ cho toàn repo, xem [`../README.md`](../README.md). File này chỉ tập trung vào backend.

## Yêu cầu

- Python `3.11` cho luồng mock / Tesseract
- `paddleocr` là tùy chọn và phải cài riêng nếu dùng `OCR_PROVIDER=paddleocr`

## Cài đặt

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

Nếu chạy trực tiếp trong thư mục `backend`:

```powershell
.\.venv\Scripts\Activate.ps1
python app.py
```

Hoặc từ root repo:

```powershell
python backend\app.py
```

## File cấu hình

Backend tự load `backend/.env` nếu file tồn tại.

Tạo nhanh từ mẫu:

```powershell
Copy-Item .env.example .env
```

Các nhóm biến quan trọng:

- Flask: `HOST`, `PORT`, `FLASK_DEBUG`
- OCR: `OCR_PROVIDER`, `GOOGLE_VISION_API_KEY`, `TESSERACT_CMD`, `OCR_MIN_CONFIDENCE`
- PaddleOCR: `PADDLEOCR_LANG`, `PADDLEOCR_USE_GPU`, `PADDLEOCR_DEVICE`
- Translate: `TRANSLATION_PROVIDER`, `LIBRETRANSLATE_URL`, `GOOGLE_TRANSLATE_API_KEY`, `GOOGLE_CLOUD_API_KEY`
- Speech: `SPEECH_PROVIDER`, `GOOGLE_SPEECH_API_KEY`
- Gemini: `LLM_PROVIDER`, `GEMINI_API_KEY`, `GEMINI_MODEL`

## Chạy local

```powershell
python app.py
```

Health check:

```powershell
Invoke-WebRequest http://127.0.0.1:5000/health | Select-Object -ExpandProperty Content
```

## Gửi sample frame

Từ root repo:

```powershell
python scripts\post_sample_frame.py --image samples\slides\slide_01.png --mock
```

Google Vision OCR + Google Translate thật:

```powershell
python scripts\post_sample_frame.py `
  --image samples\slides\slide_01.png `
  --real `
  --ocr-provider google `
  --translation-provider google
```

## Provider

### OCR

- `mock`
- `google`
- `tesseract`
- `paddleocr`

### Translation

- `mock`
- `libretranslate`
- `google`

### Speech

- `mock`
- `google`

### LLM

- `mock`
- `gemini`

## Endpoint

- `GET /health`
- `POST /pipeline`
- `POST /pipeline/frame`
- `POST /ocr`
- `POST /translate`
- `POST /speech/transcribe`
- `POST /speech/translate-text`
- `POST /speech/translate`
- `POST /speech/summarize`
- `POST /speech/ask-text`
- `WS /speech/stream`

## Chạy test

Từ root repo:

```powershell
Push-Location backend
..\.venv\Scripts\python.exe -m pytest tests -q
Pop-Location
```

Hoặc từ thư mục `backend`:

```powershell
.\.venv\Scripts\python.exe -m pytest tests -q
```

Một số test OCR thật sẽ tự skip nếu máy không có `tesseract`.

## Ghi chú

- `Dockerfile` dùng image `python:3.11-slim` và chỉ phù hợp cho luồng không phụ thuộc GPU.
- Nếu dùng `OCR_PROVIDER=paddleocr`, nên chạy backend trong môi trường riêng đã cài Paddle phù hợp với CUDA/CPU của máy.
