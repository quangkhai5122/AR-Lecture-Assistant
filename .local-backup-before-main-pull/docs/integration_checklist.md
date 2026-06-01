# Integration Checklist

## Integration 0 — Unity mock mode

- [ ] Unity scene có AR Session + XR Origin.
- [ ] Camera AR chạy trên điện thoại.
- [ ] Plane detection nhận bảng/tường.
- [ ] `useMockClient = true`.
- [ ] Bấm Scan tạo ít nhất 1 label AR.
- [ ] Clear xóa toàn bộ label.

## Integration 1 — Backend mock API

- [ ] `python backend/app.py` chạy không lỗi.
- [ ] `GET /health` trả ok.
- [ ] `POST /pipeline/frame` với `mock=true` trả blocks.
- [ ] Unity dùng `HttpPipelineClient` gọi được backend qua IP LAN.

## Integration 2 — OCR thật hoặc bán thật

- [ ] `/ocr` nhận ảnh base64.
- [ ] OCR trả bbox đúng top-left pixel coordinate.
- [ ] Có confidence.
- [ ] Kết quả lưu được vào `samples/expected_outputs/`.

## Integration 3 — Translate thật hoặc bán thật

- [ ] `/translate` dịch theo id.
- [ ] Công thức không bị dịch hỏng.
- [ ] Có glossary AI/ML.
- [ ] Có cache hoặc batch nếu latency cao.

## Integration 4 — End-to-end

- [ ] Unity capture frame.
- [ ] Backend OCR + dịch.
- [ ] Unity nhận response.
- [ ] Unity đặt label đúng vùng tương đối trên slide.
- [ ] Demo chạy được 3 lần liên tiếp không crash.

## Các lỗi phải test trước khi demo

- [ ] Backend không chạy.
- [ ] Điện thoại không vào được IP laptop.
- [ ] Không detect plane.
- [ ] OCR trả rỗng.
- [ ] Bbox nằm ngoài ảnh.
- [ ] Dịch quá dài làm label tràn.
- [ ] Mất mạng giữa demo.
