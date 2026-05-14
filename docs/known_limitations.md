# Known Limitations

## 1. Screenshot capture không phải camera frame tối ưu

MVP dùng `ScreenCapture.CaptureScreenshotAsTexture()` cho dễ chạy. Cách này có thể capture cả UI và không tối ưu cho real-time.

Cần thay bằng:

```text
ARCameraManager.TryAcquireLatestCpuImage
```

## 2. Bbox → AR plane chỉ dùng tâm bbox

Hiện tại Unity lấy tâm bbox rồi raycast vào plane. Điều này đặt label tương đối đúng vùng, nhưng chưa thật sự warp/đè khít lên đoạn chữ.

Cần cải thiện bằng:

- Detect 4 góc slide.
- Tính homography ảnh slide ↔ mặt phẳng AR.
- Map bbox corners sang world coordinates.
- Render label theo kích thước thật trên plane.

## 3. OCR mặc định là mock

Backend có optional Tesseract nhưng không bật mặc định. Tesseract không phải lựa chọn tốt nhất cho real-time AR.

Cần đánh giá:

- ML Kit Text Recognition nếu xử lý on-device.
- PaddleOCR nếu chạy backend.
- Google Cloud Vision nếu cần accuracy nhanh.

## 4. Translation mặc định là mock

Cần thay bằng service thật nếu muốn demo học thuật thuyết phục.

Lựa chọn:

- ML Kit Translation: on-device.
- Google Cloud Translation: nhanh, ổn định.
- OpenAI/LLM: tốt cho ngữ cảnh bài giảng nhưng latency/cost cao.
- NLLB/MarianMT: tự host nhưng cần tối ưu.

## 5. Chưa có speech transcript, LLM QA, Add Notes

Đây là scope sau MVP.

Gợi ý thêm module:

```text
SpeechService → TranscriptPanel
LLMService → QuestionAnswerPanel
NotesService → Markdown/Doc export
```
