# Demo Script

## Demo 1 phút

1. Mở app trên Android.
2. Chĩa camera vào slide/bảng.
3. Lia chậm để app nhận mặt phẳng.
4. Nói:

> Đây là MVP của hệ thống dịch bài giảng bằng AR. App dùng ARCore để nhận mặt phẳng bảng hoặc slide, sau đó neo bản dịch vào không gian thật.

5. Bấm `Scan`.
6. Label tiếng Việt xuất hiện trên bảng.
7. Di chuyển điện thoại sang trái/phải để chứng minh label bám vào vị trí thật.
8. Nói:

> Hiện tại backend đang chạy mock/OCR thử nghiệm. Vì toàn bộ dữ liệu đi qua JSON contract, nhóm có thể thay OCR và translation thật mà không đổi phần AR.

9. Bấm `Clear`.

## Demo 3 phút

Thêm phần giải thích pipeline:

```text
Camera frame → OCR → Formula masking → Translation → JSON blocks → AR anchor labels
```

Nhấn mạnh:

- Công thức được giữ nguyên.
- Text được dịch sang tiếng Việt.
- AR label được đặt theo bbox OCR.
- Từng module có thể phát triển song song.

## Chế độ dự phòng khi backend lỗi

Bật:

```text
useMockClient = true
```

Vẫn demo được AR overlay mà không cần backend.
