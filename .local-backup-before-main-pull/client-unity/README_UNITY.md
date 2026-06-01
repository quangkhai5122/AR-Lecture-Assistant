# Unity Client MVP

Thư mục này hiện có cấu trúc Unity project tối thiểu:

```text
Assets/
Packages/
ProjectSettings/
```

Bạn có thể mở trực tiếp thư mục `client-unity` bằng Unity Hub. Nếu Unity báo thiếu Editor, hãy cài một Editor LTS kèm module Android Build Support.

## Các script chính

- `UI/ARLectureTranslatorController.cs`: controller chính cho UI + pipeline.
- `Services/MockPipelineClient.cs`: trả kết quả giả, dùng khi chưa có backend.
- `Services/HttpPipelineClient.cs`: gọi backend Flask.
- `Services/FrameCaptureService.cs`: capture screenshot làm ảnh gửi backend.
- `AR/ARLabelPlacer.cs`: raycast bbox center vào plane, tạo anchor, tạo label.
- `AR/ScreenPlaneRaycastTester.cs`: test nhiệm vụ 1.4, raycast từ tâm màn hình/tap vào AR plane và đặt label giả.
- `AR/BillboardToCamera.cs`: xoay label về phía camera.
- `Scripts/Editor/ARLectureSceneBuilder.cs`: tạo scene Unity demo nhiệm vụ 1.4 bằng menu trong Editor.
- `Models/PipelineModels.cs`: DTO khớp contract backend.

## Tạo scene tự động cho nhiệm vụ 1.4

Sau khi mở project trong Unity, mở menu:

```text
AR Lecture Assistant → Create Task 1.4 Raycast Scene
```

Unity sẽ tạo scene:

```text
Assets/Scenes/ARLecture_Task14_Raycast.unity
```

Scene này có sẵn `AR Session`, `XR Origin`, `ARPlaneManager`, `ARRaycastManager`, `ARAnchorManager`, Canvas, reticle, nút `Place Label`, và script `ScreenPlaneRaycastTester`.

## Gợi ý setup nhanh

Xem `../docs/unity_scene_setup.md` ở root repo.
