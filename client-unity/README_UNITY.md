# Unity Client MVP

Thư mục này chứa script C# để copy vào `Assets/Scripts` của Unity project.

## Các script chính

- `UI/ARLectureTranslatorController.cs`: controller chính cho UI + pipeline.
- `Services/MockPipelineClient.cs`: trả kết quả giả, dùng khi chưa có backend.
- `Services/HttpPipelineClient.cs`: gọi backend Flask.
- `Services/FrameCaptureService.cs`: capture screenshot làm ảnh gửi backend.
- `AR/ARLabelPlacer.cs`: raycast bbox center vào plane, tạo anchor, tạo label.
- `AR/BillboardToCamera.cs`: xoay label về phía camera.
- `Models/PipelineModels.cs`: DTO khớp contract backend.

## Gợi ý setup nhanh

Xem `../docs/unity_scene_setup.md` ở root repo.
