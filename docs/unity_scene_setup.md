# Unity Scene Setup

## 1. Tạo project

Tạo Unity project mới, sau đó cài packages:

```text
AR Foundation
Google ARCore XR Plugin
XR Plugin Management
TextMeshPro
```

Bật ARCore:

```text
Edit → Project Settings → XR Plug-in Management → Android → tick ARCore
```

## 2. Copy scripts

Copy toàn bộ:

```text
client-unity/Assets/Scripts
```

vào thư mục `Assets/Scripts` trong project Unity của bạn.

## 3. Tạo AR scene

Trong Hierarchy:

```text
GameObject → XR → AR Session
GameObject → XR → XR Origin (AR)
```

Trên `XR Origin`, thêm:

```text
AR Plane Manager
AR Raycast Manager
AR Anchor Manager
```

Trên `AR Plane Manager`:

```text
Detection Mode: Vertical
```

> Slide/bảng là mặt phẳng đứng, nên ưu tiên Vertical. Nếu demo trên bàn thì dùng Horizontal hoặc Everything.

## 4. Tạo UI

Tạo Canvas `Screen Space - Overlay`, thêm:

```text
TMP_Text: StatusText
TMP_Text: DebugText
Button: ScanButton
Button: ClearButton
Image/Text nhỏ giữa màn hình làm reticle dấu +
```

## 5. Tạo controller object

Tạo empty object:

```text
ARLectureTranslatorController
```

Add components:

```text
FrameCaptureService
HttpPipelineClient
ARLabelPlacer
DebugPanelController
ARLectureTranslatorController
```

Gán references:

```text
ARLabelPlacer.raycastManager  → XR Origin / ARRaycastManager
ARLabelPlacer.anchorManager   → XR Origin / ARAnchorManager
ARLabelPlacer.arCamera        → AR Camera

DebugPanelController.debugText → DebugText

ARLectureTranslatorController.frameCaptureService → cùng object
ARLectureTranslatorController.httpPipelineClient  → cùng object
ARLectureTranslatorController.labelPlacer         → cùng object
ARLectureTranslatorController.debugPanel          → cùng object
ARLectureTranslatorController.statusText          → StatusText
ARLectureTranslatorController.scanButton          → ScanButton
ARLectureTranslatorController.clearButton         → ClearButton
```

Button OnClick:

```text
ScanButton → ARLectureTranslatorController.OnScanButtonClicked()
ClearButton → ARLectureTranslatorController.OnClearButtonClicked()
```

## 6. Chạy mock trước

Trong `ARLectureTranslatorController`:

```text
useMockClient = true
```

Build lên điện thoại. Quét bảng/tường, bấm Scan. App sẽ dùng dữ liệu giả và đặt label AR.

## 7. Chạy với backend

Chạy backend trên laptop:

```bash
cd backend
python app.py
```

Tìm IP LAN của laptop, ví dụ:

```text
192.168.1.23
```

Trong Unity, đặt:

```text
useMockClient = false
HttpPipelineClient.endpointUrl = http://192.168.1.23:5000/pipeline/frame
backendMockMode = true
```

> Điện thoại và laptop phải cùng mạng Wi-Fi. Tường lửa có thể chặn port 5000.

## 8. Sau MVP

TODO quan trọng:

- Thay `FrameCaptureService` bằng ARCameraManager CPU image.
- Tối ưu ảnh gửi backend: resize 1280px hoặc thấp hơn.
- Chỉ OCR mỗi 0.5–1.5 giây, không OCR mỗi frame.
- Dùng tracking ID/cache để label không nhấp nháy.
- Mapping bbox vào mặt phẳng bằng homography để overlay sát chữ hơn.
