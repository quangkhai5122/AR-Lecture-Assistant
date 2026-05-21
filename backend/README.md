# AR Lecture Translator MVP

MVP này là bộ khung cho hệ thống **dịch slide/bài giảng bằng AR trên mobile**:

- Unity + AR Foundation + ARCore: camera AR, raycast, anchor, world-space translation labels.
- Backend Flask: `/pipeline/frame` nhận ảnh base64, chạy OCR mock/optional, giữ công thức, dịch mock/optional, trả JSON.
- JSON contracts: schema thống nhất.
- Docs: hướng dẫn setup Unity, API, tích hợp, demo và checklist.

> Mục tiêu của MVP: chạy được end-to-end bằng mock trước. Sau đó từng thành viên thay mock bằng OCR/translation thật.

## Cài PaddleOCR và LibreTranslate
```bash
conda create -n paddleocr_gpu python=3.10 -y
conda activate paddleocr_gpu
python -m pip install --upgrade pip
python -m pip install paddlepaddle-gpu==3.2.0 -i https://www.paddlepaddle.org.cn/packages/stable/cu118/   # Phải chạy nvidia-smi trước để xem có tương thích phiên bản cuda không.
pip install paddleocr
pip install -r requirements.txt
python app.py
```

## Chạy PaddleOCR + Google Cloud Translation

Không hardcode API key vào repo. Đặt key trong biến môi trường trước khi chạy backend:

```powershell
conda activate D:\conda_envs\paddleocr_gpu
cd D:\Python_Project\AR_Lecture_Assistant

$env:FLASK_DEBUG="0"
$env:OCR_PROVIDER="paddleocr"
$env:PADDLEOCR_USE_GPU="1"
$env:PADDLEOCR_LANG="en"

$env:TRANSLATION_PROVIDER="google"
$env:GOOGLE_TRANSLATE_API_KEY="<your-google-translate-api-key>"
$env:GOOGLE_TRANSLATE_SOURCE_LANGUAGE="en"

python backend\app.py
```

Test end-to-end:

```powershell
python scripts\post_sample_frame.py `
  --image samples\slides\slide_01.png `
  --real `
  --ocr-provider paddleocr `
  --translation-provider google `
  --timeout 240
```

## Chạy OCR + Translate theo contract json

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