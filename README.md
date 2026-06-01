# AR Lecture Assistant

AR Lecture Assistant là một project Unity + Flask để dịch nội dung slide/bảng trong ngữ cảnh bài giảng. Ứng dụng chụp frame từ camera AR, gửi ảnh sang backend để OCR và dịch, rồi hiển thị bản dịch trực tiếp trên plane trong không gian AR. Repo hiện tại cũng có thêm nhánh transcript cho phần giảng nói: ghi âm, dịch câu nói, lưu note, tóm tắt và hỏi đáp bằng Gemini.

## Trạng thái repo hiện tại

- Repo gốc **đã là Unity project hoàn chỉnh** (`Assets/`, `Packages/`, `ProjectSettings/`).
- Backend Flask nằm trong [`backend/`](backend/).
- JSON contract nằm trong [`contracts/`](contracts/).
- Script gửi sample request nằm trong [`scripts/`](scripts/).
- Thư mục [`client-unity/`](client-unity/) chỉ là snapshot script để tham chiếu, không phải project Unity chính.

## Tính năng đang có

- Quét slide/bảng và đặt translation overlay lên nội dung OCR.
- Mặc định khi mở app sẽ hiện đủ 5 nút chính: `Hide VN` / `Show VN`, `Transcript`, `Quét`, `Dịch` / `Dịch lại` / `Thử lại`, và `Xóa`.
- Ưu tiên capture từ AR camera raw để tránh đưa UI overlay vào ảnh OCR, fallback sang screenshot khi thiết bị không hỗ trợ CPU image.
- Backend trả thêm `document_surface` để Unity map bbox OCR về bề mặt AR và vẽ outline surface.
- Spatial mapper projects detected document corners to the locked AR plane, keeps label nudges bounded near OCR boxes, and attaches anchors to the common ARPlane when available.
- Giữ nguyên công thức toán khi dịch.
- Gộp block OCR theo dòng để overlay dễ đọc hơn.
- Subtitle cho dòng dịch chính.
- Tùy chọn tap-to-focus một label dịch để đọc ở panel lớn hơn, vẫn tắt mặc định để tránh mở thêm panel khi chưa cần.
- Nút `Hide VN` / `Show VN` để ẩn hoặc hiện toàn bộ bản dịch trên slide được bật trong full-feature mode.
- Speech transcript với các chế độ:
  - mock transcript trong Editor;
  - Google Speech-to-Text qua REST hoặc SDK;
  - dịch câu nói bằng Gemini;
  - lưu note, export note, xóa note;
  - tóm tắt note bằng Gemini;
  - hỏi đáp trên một đoạn text đã chọn.
- Chế độ mock và real provider cho backend.

## UI modes

- Scene chính: [`Assets/Scenes/MainScene.unity`](Assets/Scenes/MainScene.unity).
- UI mặc định hiện đúng 5 nút trên màn hình: `Hide VN`, `Transcript`, `Quét`, `Dịch`, `Xóa`.
- `Freeze` và debug toggle vẫn còn trong code nhưng bị ẩn mặc định qua `showAdvancedControls = false`.
- Compact two-button demo vẫn được giữ trong code: bật `useCompactDemoControls` trên `ButtonController` / `ARLectureVisualPolish`, tắt `showTranscriptControl`, `showTranslationVisibilityButton`, và `showAdvancedControls` nếu cần quay lại chế độ trình diễn tối giản.
- AR flow mặc định: quét plane, lock surface, vẽ outline, capture frame, OCR/dịch qua backend, đặt label trong world space.
- `MainScene` gắn sẵn `FrameCaptureService`, `HttpPipelineClient`, `ARSurfaceLockController`, và `ARSurfaceOutlineRenderer` để có thể cấu hình backend URL / capture path trong Inspector trước khi build Android.
- Checklist kiểm thử nằm ở [`docs/TESTING_CHECKLIST.md`](docs/TESTING_CHECKLIST.md), kịch bản demo nằm ở [`docs/DEMO_SCRIPT.md`](docs/DEMO_SCRIPT.md), và audit Definition of Done nằm ở [`docs/AR_UPGRADE_ACCEPTANCE_AUDIT.md`](docs/AR_UPGRADE_ACCEPTANCE_AUDIT.md).

## Cấu trúc thư mục

```text
AR-Lecture-Assistant/
├── Assets/                    # Unity project chính
├── Packages/
├── ProjectSettings/
├── backend/                   # Flask backend
├── client-unity/              # Snapshot script tham chiếu
├── contracts/                 # JSON schema / sample payload
├── samples/                   # Ảnh mẫu để test pipeline
├── scripts/                   # Helper script chạy backend / post sample frame
└── README.md
```

## Yêu cầu cài đặt

### Bắt buộc

- Unity `2022.3.62f3`
- Android Build Support trong Unity Hub nếu muốn build APK
- Python `3.11` cho backend local

### Tùy chọn theo provider

- **Tesseract OCR** nếu muốn OCR thật tại local
- **PaddleOCR** nếu muốn OCR bằng Paddle trên môi trường riêng
- **Google Translate API key** nếu muốn dịch bằng Google
- **Google Speech** credentials hoặc API key nếu muốn speech-to-text thật
- **Gemini API key** nếu muốn dịch speech / tóm tắt / hỏi đáp thật

## Chạy nhanh theo mock mode

Đây là đường đi ngắn nhất để chạy end-to-end mà không cần API key.

### 1. Tạo môi trường Python và cài backend

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r backend\requirements.txt
```

### 2. Chạy backend

```powershell
python backend\app.py
```

Backend mặc định chạy ở `http://127.0.0.1:5000`.

### 3. Kiểm tra backend

```powershell
Invoke-WebRequest http://127.0.0.1:5000/health | Select-Object -ExpandProperty Content
```

### 4. Gửi thử một frame mẫu

```powershell
python scripts\post_sample_frame.py --image samples\slides\slide_01.png --mock
```

### 5. Mở Unity project

1. Mở Unity Hub.
2. `Add project from disk` và chọn thư mục repo gốc `AR-Lecture-Assistant`.
3. Mở scene [`Assets/Scenes/MainScene.unity`](Assets/Scenes/MainScene.unity).
4. Nhấn Play để kiểm tra UI / backend flow trong Editor.

### 6. Build APK bằng batchmode

Repo có build method `AndroidBuild.BuildApk` cho CI/local smoke build:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath "$PWD" `
  -buildTarget Android `
  -executeMethod AndroidBuild.BuildApk `
  -outputPath 'Builds\Android\ARLectureAssistant.apk' `
  -logFile 'unity-android-build.log'
```

Thư mục `Builds/` được ignore. Nếu build không vào Gradle và log báo `LicensingClient` IPC timeout, mở Unity Hub/Editor thủ công để refresh license rồi chạy lại lệnh.

## Cấu hình backend URL cho Unity

`HttpPipelineClient` hiện có default trong code là LAN URL mẫu cho Android, còn `MainScene` hiện đang serialize theo máy build hiện tại:

- `backendBaseUrl = http://192.168.1.8:5000`
- `endpointUrl = http://192.168.1.8:5000/pipeline/frame`

Nhưng ở runtime, luồng chính ưu tiên `backendBaseUrl`. Vì vậy:

- Nếu chạy Unity Editor trên cùng máy với backend: đặt `backendBaseUrl` thành `http://127.0.0.1:5000`
- Nếu chạy trên điện thoại Android: đặt `backendBaseUrl` thành `http://<LAN-IP-cua-may-chay-backend>:5000`
- Nếu IP Wi-Fi của máy build thay đổi, phải sửa lại `backendBaseUrl` trong `UICanvas` trước khi build APK mới.

Có hai cách cấu hình:

1. Chỉnh `HttpPipelineClient` đã gắn trên `UICanvas` trong [`Assets/Scenes/MainScene.unity`](Assets/Scenes/MainScene.unity), rồi sửa `backendBaseUrl` trong Inspector.
2. Hoặc sửa default trực tiếp trong [`Assets/Scripts/Services/HttpPipelineClient.cs`](Assets/Scripts/Services/HttpPipelineClient.cs).

Lưu ý: `ButtonController` và `SpeechTranscriptController` vẫn có fallback tự `AddComponent<HttpPipelineClient>()` nếu component bị gỡ khỏi scene, nhưng demo scene hiện đã gắn sẵn component để tránh cấu hình backend URL bị ẩn.

Trong full-feature mode mặc định, `ButtonController` gọi backend thật với `backendMockMode=false`, `ocrProvider=google` và `translationProvider=google`. Backend cần Google Cloud API key có bật Cloud Vision API và Cloud Translation API.

## Cài đặt backend chi tiết

### Cấu hình `.env`

Backend tự đọc file [`backend/.env`](backend/.env) khi khởi động. Repo đã thêm mẫu tại [`backend/.env.example`](backend/.env.example).

```powershell
Copy-Item backend\.env.example backend\.env
```

Sau đó sửa các biến cần dùng.

### OCR và dịch thật bằng Google APIs

Luồng mặc định dùng Google Cloud Vision để đọc chữ trên slide và Google Cloud Translation để dịch sang tiếng Việt.

1. Bật Cloud Vision API và Cloud Translation API trong Google Cloud project.
2. Tạo API key có quyền gọi hai API này.
3. Cấu hình tối thiểu:

```env
OCR_PROVIDER=google
TRANSLATION_PROVIDER=google
GOOGLE_VISION_API_KEY=<your-google-cloud-api-key>
GOOGLE_TRANSLATE_API_KEY=<your-google-cloud-api-key>
```

Nếu dùng chung một key cho cả hai API, có thể đặt `GOOGLE_CLOUD_API_KEY` thay cho hai biến riêng.

Test với sample:

```powershell
python scripts\post_sample_frame.py `
  --image samples\slides\slide_01.png `
  --real `
  --ocr-provider google `
  --translation-provider google
```

### OCR thật bằng Tesseract

Tesseract là lựa chọn local đơn giản nhất cho OCR thật.

1. Cài binary Tesseract trên máy.
2. Nếu `tesseract.exe` không nằm trong `PATH`, đặt `TESSERACT_CMD` trong `backend/.env`.
3. Cấu hình tối thiểu:

```env
OCR_PROVIDER=tesseract
OCR_MIN_CONFIDENCE=0.22
TRANSLATION_PROVIDER=google
GOOGLE_TRANSLATE_API_KEY=<your-google-cloud-api-key>
```

Test với sample:

```powershell
python scripts\post_sample_frame.py `
  --image samples\slides\slide_01.png `
  --real `
  --ocr-provider tesseract `
  --translation-provider google
```

### OCR bằng PaddleOCR

`paddleocr` không nằm trong `backend/requirements.txt`. Repo cố ý tách phần này vì phụ thuộc CUDA / môi trường GPU.

Ví dụ flow riêng:

```powershell
conda create -n paddleocr_gpu python=3.10 -y
conda activate paddleocr_gpu
python -m pip install --upgrade pip
pip install paddleocr
pip install -r backend\requirements.txt
python backend\app.py
```

Khi dùng PaddleOCR, cấu hình các biến như:

```env
OCR_PROVIDER=paddleocr
PADDLEOCR_LANG=en
PADDLEOCR_USE_GPU=1
PADDLEOCR_DEVICE=gpu
```

### Dịch bằng Google Translate

```env
TRANSLATION_PROVIDER=google
GOOGLE_TRANSLATE_API_KEY=<your-key>
GOOGLE_TRANSLATE_SOURCE_LANGUAGE=en
```

### Speech-to-Text bằng Google

Backend hỗ trợ hai cách:

- dùng `GOOGLE_SPEECH_API_KEY` hoặc `GOOGLE_CLOUD_SPEECH_API_KEY`;
- hoặc dùng `google-cloud-speech` + `GOOGLE_APPLICATION_CREDENTIALS`.

Ví dụ:

```env
SPEECH_PROVIDER=google
GOOGLE_SPEECH_API_KEY=<your-key>
```

### Gemini cho dịch câu nói / summary / ask text

```env
LLM_PROVIDER=gemini
GEMINI_API_KEY=<your-key>
GEMINI_MODEL=gemini-2.5-flash-lite
```

## Chạy test backend

```powershell
Push-Location backend
..\.venv\Scripts\python.exe -m pytest tests -q
Pop-Location
```

Repo hiện có test cho:

- mock pipeline;
- OCR endpoint;
- formula masking / restore;
- OCR post-processing;
- translation cache;
- speech endpoint mock;
- Gemini adapter mock / request format.

Một số test OCR thật sẽ tự skip nếu máy không có binary `tesseract`.

## Scene và workflow chính trong Unity

Scene build hiện tại là [`Assets/Scenes/MainScene.unity`](Assets/Scenes/MainScene.unity). `EditorBuildSettings` đang bật scene này làm scene chạy chính.

Luồng sử dụng:

1. `Scan` để bật plane detection
2. `Translate` để chụp frame và gọi backend
3. App đặt overlay dịch lên nội dung OCR
4. `Hide VN` để ẩn toàn bộ bản dịch, `Show VN` để bật lại
5. `Clear` để xóa overlay
6. Transcript modal cho speech/note/summary hoạt động độc lập với luồng slide

## Endpoint backend chính

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

Schema tham chiếu nằm trong [`contracts/`](contracts/).

## File quan trọng nên đọc trước

- [`Assets/Scripts/UI/ButtonController.cs`](Assets/Scripts/UI/ButtonController.cs)
- [`Assets/Scripts/AR/ARLabelPlacer.cs`](Assets/Scripts/AR/ARLabelPlacer.cs)
- [`Assets/Scripts/UI/SpeechTranscriptController.cs`](Assets/Scripts/UI/SpeechTranscriptController.cs)
- [`Assets/Scripts/Services/HttpPipelineClient.cs`](Assets/Scripts/Services/HttpPipelineClient.cs)
- [`backend/app.py`](backend/app.py)
- [`backend/services/pipeline_service.py`](backend/services/pipeline_service.py)
- [`backend/tests/test_pipeline.py`](backend/tests/test_pipeline.py)

## Ghi chú

- Thư mục `docs/` chứa checklist kiểm thử, kịch bản demo và trạng thái triển khai theo `AR_UPGRADE_MASTER_PLAN.md`.
- Nếu chỉ muốn lấy bộ script cũ để nhúng vào một project khác, xem [`client-unity/README_UNITY.md`](client-unity/README_UNITY.md), nhưng source chạy chính vẫn là thư mục `Assets/` ở root repo.
