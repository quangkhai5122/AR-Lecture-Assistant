# client-unity

Thư mục này chỉ giữ một snapshot script Unity để tham chiếu.

## Quan trọng

- Project Unity đang chạy thật nằm ở **root repo**:
  - [`../Assets/`](../Assets/)
  - [`../Packages/`](../Packages/)
  - [`../ProjectSettings/`](../ProjectSettings/)
- Scene và wiring hiện tại phải xem trong [`../Assets/Scenes/MainScene.unity`](../Assets/Scenes/MainScene.unity).
- Các script trong `client-unity/Assets/Scripts/` không phải nguồn chuẩn duy nhất cho trạng thái mới nhất của app.

## Khi nào dùng thư mục này

Chỉ dùng khi bạn muốn:

- tham khảo một bản script-only snapshot;
- copy nhanh một vài file C# sang project khác để thử nghiệm.

Nếu bắt đầu cài đặt hoặc chạy dự án này từ đầu, hãy dùng README gốc ở [`../README.md`](../README.md).
