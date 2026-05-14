# AR Lecture Translator MVP

MVP này là bộ khung cho hệ thống **dịch slide/bài giảng bằng AR trên mobile**:

- Unity + AR Foundation + ARCore: camera AR, raycast, anchor, world-space translation labels.
- Backend Flask: `/pipeline/frame` nhận ảnh base64, chạy OCR mock/optional, giữ công thức, dịch mock/optional, trả JSON.
- JSON contracts: schema thống nhất.
- Docs: hướng dẫn setup Unity, API, tích hợp, demo và checklist.

> Mục tiêu của MVP: chạy được end-to-end bằng mock trước. Sau đó từng thành viên thay mock bằng OCR/translation thật.

## Cấu trúc

```text
ar-lecture-translator-mvp/
├── client-unity/
│   └── Assets/Scripts/
├── backend/
├── contracts/
├── samples/
├── docs/
└── scripts/
```

## Chạy backend mock

```bash
cd backend
python -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
python app.py
```

Test nhanh:

```bash
curl http://127.0.0.1:5000/health
```

Gọi pipeline bằng sample image:

```bash
python ../scripts/post_sample_frame.py --image ../samples/slides/slide_01.png
```

## Setup Unity

Xem: [`docs/unity_scene_setup.md`](docs/unity_scene_setup.md)

Tóm tắt:

1. Tạo Unity project.
2. Cài `AR Foundation`, `Google ARCore XR Plugin`, `XR Plugin Management`, `TextMeshPro`.
3. Copy thư mục `client-unity/Assets/Scripts` vào project Unity.
4. Tạo scene gồm `AR Session`, `XR Origin (AR)`, `AR Plane Manager`, `AR Raycast Manager`, `AR Anchor Manager`.
5. Tạo UI có nút `Scan`, `Clear`, `StatusText`, `DebugText`.
6. Gắn script `ARLectureTranslatorController` vào một empty object.

## Luồng MVP

```text
Unity App
  ↓ capture screenshot / frame
POST /pipeline/frame
  ↓
Backend
  ├─ OCRService: mock hoặc Tesseract optional
  ├─ FormulaService: mask/restore công thức
  ├─ TranslationService: mock hoặc LibreTranslate optional
  └─ PipelineService: merge kết quả
  ↓
Unity nhận blocks
  ↓
Raycast bbox center vào AR plane
  ↓
Tạo anchor + world-space label
```

## Chế độ mock và real

- `MockPipelineClient.cs`: không cần backend, trả dữ liệu giả trong Unity.
- `HttpPipelineClient.cs`: gọi backend thật.
- Backend mặc định vẫn trả OCR/dịch mock để demo ổn định.

## Việc cần cải thiện sau MVP

Tìm các comment `TODO(MVP)` trong code. Các việc lớn:

- Thay screenshot capture bằng `ARCameraManager.TryAcquireLatestCpuImage`.
- Thay OCR mock bằng ML Kit / Google Vision / PaddleOCR / Tesseract pipeline ổn định.
- Thay dịch mock bằng ML Kit Translation / Google Translate / OpenAI / NLLB.
- Mapping bbox 2D → plane 3D chính xác hơn bằng homography/pose estimation.
- Làm label overlap avoidance.
- Cache OCR/translation để giảm latency.
- Thêm LLM QA + Add Notes.
