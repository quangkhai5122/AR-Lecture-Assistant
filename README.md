# AR Lecture Assistant

AR Lecture Assistant là dự án Unity AR + Flask backend dùng để hỗ trợ người học trong bối cảnh bài giảng. Ứng dụng quét bảng hoặc slide bằng camera AR, gửi frame về backend để phát hiện bề mặt tài liệu, OCR và dịch nội dung sang tiếng Việt, sau đó ghim bản dịch lên đúng vị trí trong không gian AR. Repo cũng có luồng transcript giọng nói để ghi chú, dịch câu nói, tóm tắt và hỏi đáp bằng Gemini.

## Trạng thái repo

- Project Unity chính nằm ngay tại root repo: `Assets/`, `Packages/`, `ProjectSettings/`.
- Scene chạy chính là `Assets/Scenes/MainScene.unity`; `ProjectSettings/EditorBuildSettings.asset` đang bật scene này.
- Backend Flask nằm trong `backend/`.
- JSON schema và payload mẫu nằm trong `contracts/`.
- Ảnh test pipeline nằm trong `samples/`.
- Tài liệu kiểm thử, kịch bản demo và audit nằm trong `docs/`.
- `client-unity/` chỉ là snapshot script tham chiếu, không phải source Unity chính.

## Tính năng chính

- Quét và khóa mặt bảng/slide bằng AR Foundation/ARCore.
- Capture frame từ AR camera hoặc screenshot fallback để gửi OCR.
- Backend phát hiện `document_surface` bằng OpenCV, có fallback theo union bbox OCR.
- Hỗ trợ crop/warp bề mặt tài liệu trước khi OCR, rồi map bbox về ảnh gốc.
- OCR provider: `mock`, `google`, `tesseract`, `paddleocr`.
- Translation provider: `mock`, `libretranslate`, `google`.
- Giữ nguyên công thức toán bằng heuristic masking/restoring.
- Gộp text block cùng dòng để overlay dễ đọc hơn.
- Ghim translation label trong world space; có screen-space fallback khi cần.
- Nút `Hide VN` / `Show VN` để ẩn hoặc hiện toàn bộ bản dịch.
- Transcript giọng nói với Google Speech-to-Text hoặc mock mode.
- Dịch transcript, tóm tắt note và giải thích dòng đã chọn bằng Gemini hoặc mock LLM.
- Lưu, export và xóa note Markdown trong `Application.persistentDataPath`.

## Kiến trúc ngắn gọn

```text
Unity AR app
  ButtonController
  FrameCaptureService
  HttpPipelineClient
        |
        v
Flask backend
  /pipeline/frame
  DocumentSurfaceService -> OCRService -> FormulaService -> TranslationService
        |
        v
PipelineResponse
  document_surface + translated blocks + latency + provider metadata
        |
        v
Unity
  ARDocumentSurfaceMapper -> ARAnchorPlacer -> ARLabelPlacer
```

Luồng transcript dùng cùng `HttpPipelineClient` để gọi các endpoint `/speech/*` hoặc WebSocket `/speech/stream`.

## Cấu trúc thư mục

```text
AR-Lecture-Assistant/
├── Assets/                  # Unity project chính
│   ├── Scenes/              # MainScene.unity
│   ├── Scripts/AR/          # Surface lock, raycast, anchor, label placement
│   ├── Scripts/Services/    # Backend HTTP, capture, speech, notes
│   ├── Scripts/UI/          # UI state, controls, transcript, debug
│   └── Plugins/Android/     # AndroidManifest.xml
├── Packages/                # Unity package manifest
├── ProjectSettings/         # Unity settings
├── backend/                 # Flask backend
│   ├── app.py
│   ├── requirements.txt
│   ├── services/
│   └── tests/
├── contracts/               # JSON schemas and sample responses
├── docs/                    # Demo, checklist, implementation audit
├── samples/                 # Sample slide images
├── scripts/                 # Helper scripts
└── client-unity/            # Script-only snapshot tham chiếu
```

## Yêu cầu

### Unity và Android

- Unity `2022.3.62f3`.
- Android Build Support nếu build APK.
- Thiết bị Android có ARCore nếu chạy AR thật.
- Project dùng `com.unity.xr.arfoundation` và `com.unity.xr.arcore` phiên bản `5.2.2`.
- Android manifest đã khai báo `INTERNET`, `CAMERA`, `RECORD_AUDIO` và `android:usesCleartextTraffic="true"`.
- Android min SDK hiện là `24`.

### Backend

- Python `3.11`.
- Dependencies trong `backend/requirements.txt`.
- Tesseract binary nếu dùng `OCR_PROVIDER=tesseract`.
- PaddleOCR cài riêng nếu dùng `OCR_PROVIDER=paddleocr`.
- API key Google/Gemini nếu chạy real provider.

## Chạy nhanh bằng mock mode

Mock mode là đường đi ngắn nhất để test end-to-end mà không cần API key.

### 1. Cài backend

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r backend\requirements.txt
```

### 2. Chạy Flask backend

```powershell
$env:PORT = "5000"
python backend\app.py
```

Backend mặc định dùng `HOST=0.0.0.0`, `PORT=5000` nếu không có biến môi trường hoặc `backend/.env`.

Lưu ý: `backend/.env.example` đang đặt `PORT=5050`. Nếu copy file này thành `backend/.env`, hãy đổi lại `PORT=5000` hoặc cấu hình Unity/script test sang port tương ứng.

### 3. Kiểm tra health

```powershell
Invoke-RestMethod http://127.0.0.1:5000/health
```

### 4. Gửi sample frame

```powershell
python scripts\post_sample_frame.py --image samples\slides\slide_01.png --mock
```

Nếu backend chạy port khác:

```powershell
python scripts\post_sample_frame.py `
  --url http://127.0.0.1:5050/pipeline/frame `
  --image samples\slides\slide_01.png `
  --mock
```

## Mở Unity project

1. Mở Unity Hub.
2. Chọn `Add project from disk`.
3. Chọn thư mục root `AR-Lecture-Assistant`, không chọn `client-unity/`.
4. Mở `Assets/Scenes/MainScene.unity`.
5. Trong Editor, cấu hình component `HttpPipelineClient` dùng `http://127.0.0.1:5000` nếu backend chạy cùng máy.
6. Nhấn Play để kiểm tra UI và flow backend.

Flow chính trong app:

1. `Quét` để tìm và khóa mặt bảng/slide.
2. `Dịch` để capture frame, gọi backend và đặt label dịch.
3. `Hide VN` / `Show VN` để ẩn hoặc hiện overlay tiếng Việt.
4. `Transcript` để bật/tắt modal ghi âm, note và summary.
5. `Xóa` để dọn overlay và reset trạng thái.

## Cấu hình backend URL cho Android

Trên Android, `127.0.0.1` là chính điện thoại, không phải laptop chạy backend. Vì vậy URL phải là IP LAN của máy chạy Flask, ví dụ:

```text
http://192.168.1.20:5000
```

Trong `Assets/Scripts/Services/HttpPipelineClient.cs`, default hiện là:

```text
http://192.168.1.7:5000
```

Có hai cách đổi URL:

- Sửa component `HttpPipelineClient` trong scene. Nếu `useDefaultBackendBaseUrl` đang bật, hãy tắt flag này trước khi nhập `backendBaseUrl` trong Inspector.
- Hoặc sửa constant `DefaultAndroidLanBackendUrl` trong `Assets/Scripts/Services/HttpPipelineClient.cs` rồi build lại.

Checklist nhanh khi điện thoại không gọi được backend:

- Laptop và điện thoại cùng Wi-Fi.
- Flask đang listen `0.0.0.0:5000`, không chỉ `127.0.0.1`.
- Firewall cho phép TCP port `5000`.
- Unity không còn trỏ về `127.0.0.1` hoặc `localhost`.
- Nếu dùng HTTP, Android manifest cần `android:usesCleartextTraffic="true"`; repo hiện đã bật.

## Provider backend

Backend đọc `backend/.env` khi khởi động. Các biến trong môi trường hệ thống sẽ được ưu tiên hơn file `.env`.

Tạo file cấu hình:

```powershell
Copy-Item backend\.env.example backend\.env
```

### Mock provider

Mock provider phù hợp cho smoke test, demo luồng UI và test contract.

```env
OCR_PROVIDER=mock
TRANSLATION_PROVIDER=mock
SPEECH_PROVIDER=mock
LLM_PROVIDER=mock
PORT=5000
```

Khi gửi request có `"mock": true`, backend sẽ ép OCR/translation/speech/LLM về mock tương ứng.

### Google Vision OCR + Google Translate

Unity `ButtonController` đang mặc định real mode với `ocrProvider=google`, `translationProvider=google`, `backendMockMode=false`. Cấu hình tối thiểu:

```env
PORT=5000
OCR_PROVIDER=google
TRANSLATION_PROVIDER=google
GOOGLE_VISION_API_KEY=<your-google-cloud-api-key>
GOOGLE_TRANSLATE_API_KEY=<your-google-cloud-api-key>
```

Có thể dùng `GOOGLE_CLOUD_API_KEY` thay cho hai key riêng nếu key đó được cấp quyền cho cả Cloud Vision API và Cloud Translation API.

Test real provider:

```powershell
python scripts\post_sample_frame.py `
  --image samples\slides\slide_01.png `
  --real `
  --ocr-provider google `
  --translation-provider google
```

### Tesseract OCR + translation provider

```env
PORT=5000
OCR_PROVIDER=tesseract
OCR_MIN_CONFIDENCE=0.22
TRANSLATION_PROVIDER=google
GOOGLE_TRANSLATE_API_KEY=<your-google-cloud-api-key>
TESSERACT_CMD=C:\Program Files\Tesseract-OCR\tesseract.exe
```

Nếu `tesseract.exe` đã nằm trong `PATH`, có thể bỏ `TESSERACT_CMD`.

Test:

```powershell
python scripts\post_sample_frame.py `
  --image samples\slides\slide_01.png `
  --real `
  --ocr-provider tesseract `
  --translation-provider google
```

### LibreTranslate

```env
PORT=5000
TRANSLATION_PROVIDER=libretranslate
LIBRETRANSLATE_URL=http://localhost:5001/translate
LIBRETRANSLATE_API_KEY=
```

`TranslationService` hiện chỉ hỗ trợ `mock`, `libretranslate`, `google`. Một vài script demo cũ còn nhắc `mymemory`; provider đó không còn nằm trong service hiện tại.

### PaddleOCR

`paddleocr` không nằm trong `backend/requirements.txt` vì phụ thuộc CPU/GPU/CUDA. Cài nó trong môi trường riêng, sau đó chạy backend bằng môi trường đó.

Ví dụ:

```powershell
conda create -n paddleocr_gpu python=3.10 -y
conda activate paddleocr_gpu
python -m pip install --upgrade pip
pip install paddleocr
pip install -r backend\requirements.txt
python backend\app.py
```

Cấu hình:

```env
PORT=5000
OCR_PROVIDER=paddleocr
OCR_FALLBACK_PROVIDER=tesseract
PADDLEOCR_LANG=en
PADDLEOCR_USE_GPU=1
PADDLEOCR_DEVICE=gpu
```

### Speech-to-Text và Gemini

Google Speech-to-Text qua REST API key:

```env
PORT=5000
SPEECH_PROVIDER=google
GOOGLE_SPEECH_API_KEY=<your-google-speech-key>
```

Hoặc dùng Google client SDK với `GOOGLE_APPLICATION_CREDENTIALS` ở môi trường hệ thống.

Gemini cho dịch transcript, summary và hỏi đáp:

```env
PORT=5000
LLM_PROVIDER=gemini
GEMINI_API_KEY=<your-gemini-key>
GEMINI_MODEL=gemini-2.5-flash-lite
GEMINI_TEMPERATURE=0.2
```

## Endpoint backend

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

Schema tham chiếu nằm trong `contracts/`.

## Request pipeline tối thiểu

```json
{
  "frame_id": "slide_01",
  "target_language": "vi",
  "mock": true,
  "image_width": 1280,
  "image_height": 720
}
```

Real mode cần `image_base64`:

```json
{
  "frame_id": "slide_01",
  "target_language": "vi",
  "mock": false,
  "image_base64": "<base64-image>",
  "image_width": 1280,
  "image_height": 720,
  "ocr_provider": "google",
  "translation_provider": "google",
  "use_surface_crop_for_ocr": true
}
```

Response chính gồm:

- `document_surface`: 4 góc bề mặt tài liệu trong image-space.
- `blocks`: text gốc, text dịch, bbox, confidence và loại block `text` / `formula` / `mixed`.
- `provider`: OCR và translation provider thật sự được dùng.
- `mock_used`: request có dùng mock hay không.
- `warnings`: cảnh báo provider, crop, cache hoặc fallback.
- `latency_ms`: `surface_detection`, `ocr`, `translation`, `total`.

## Chạy test backend

```powershell
Push-Location backend
..\.venv\Scripts\python.exe -m pytest tests -q
Pop-Location
```

Một số test OCR thật sẽ tự skip nếu máy không có Tesseract binary. Các test phát hiện bề mặt tài liệu cần import được `cv2` từ `opencv-python-headless`; nếu môi trường Python thiếu OpenCV, chúng sẽ rơi về fallback và có thể fail. Xem `docs/IMPLEMENTATION_STATUS.md` để biết bằng chứng verify đã ghi nhận trước đó.

## Build APK

Build thủ công qua Unity Editor hoặc chạy batchmode:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath "$PWD" `
  -buildTarget Android `
  -executeMethod AndroidBuild.BuildApk `
  -outputPath 'Builds\Android\ARLectureAssistant.apk' `
  -logFile 'unity-android-build.log'
```

Nếu Unity dừng ở lỗi licensing, mở Unity Hub/Editor một lần để refresh license rồi chạy lại lệnh. Thư mục `Builds/` được ignore.

Trước khi build Android demo:

- Cập nhật `HttpPipelineClient.backendBaseUrl` hoặc `DefaultAndroidLanBackendUrl` sang IP LAN hiện tại.
- Chạy backend với `HOST=0.0.0.0`, `PORT=5000`.
- Bật API key nếu Unity đang dùng real provider.
- Kiểm tra `docs/TESTING_CHECKLIST.md`.

## File nên đọc khi phát triển

- `Assets/Scripts/UI/ButtonController.cs`: điều phối scan/translate/clear và provider backend.
- `Assets/Scripts/Services/HttpPipelineClient.cs`: contract HTTP/WebSocket giữa Unity và Flask.
- `Assets/Scripts/Services/FrameCaptureService.cs`: capture AR camera hoặc screenshot fallback.
- `Assets/Scripts/AR/ARSurfaceLockController.cs`: khóa mặt bảng/slide.
- `Assets/Scripts/AR/ARLabelPlacer.cs`: map OCR bbox và đặt label AR.
- `Assets/Scripts/UI/SpeechTranscriptController.cs`: transcript, note, summary.
- `backend/app.py`: Flask routes và validation.
- `backend/services/pipeline_service.py`: orchestration OCR -> formula -> translation.
- `backend/services/ocr_service.py`: OCR providers và post-process.
- `backend/services/document_surface_service.py`: phát hiện/crop/map bề mặt tài liệu.
- `backend/tests/test_pipeline.py`: coverage chính của backend và contract.

## Tài liệu liên quan

- `docs/DEMO_SCRIPT.md`: kịch bản demo.
- `docs/TESTING_CHECKLIST.md`: checklist kiểm thử thủ công.
- `docs/IMPLEMENTATION_STATUS.md`: trạng thái triển khai và bằng chứng kiểm thử.
- `docs/AR_UPGRADE_ACCEPTANCE_AUDIT.md`: audit theo Definition of Done.
- `AR_UPGRADE_MASTER_PLAN.md`: kế hoạch nâng cấp AR.

## Giới hạn hiện tại

- Các kiểm thử trong repo xác nhận backend, contract và compile Unity, nhưng trải nghiệm AR thật vẫn cần xác nhận trên thiết bị Android ARCore.
- PaddleOCR cần môi trường riêng, không được cài mặc định qua `requirements.txt`.
- Real OCR/translation/speech/LLM phụ thuộc API key và quota ngoài repo.
- `client-unity/` không phải project chạy chính; dùng root Unity project để tránh lệch scene và wiring.
