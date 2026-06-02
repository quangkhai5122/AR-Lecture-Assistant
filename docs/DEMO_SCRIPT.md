# AR Lecture Assistant Demo Script

## Setup

- Android ARCore device in landscape orientation.
- Backend running on the demo laptop and reachable from the phone over LAN.
- Unity build configured with backend mock mode disabled and Google Vision OCR + Google Translate credentials available in the backend environment.
- Slide or board visible with high contrast text.

## Two-Minute Flow

1. Open the app and point the camera at the lecture slide.
2. Say: "The app starts with the five core controls visible: Hide VN, Transcript, Scan, Translate, and Clear."
3. Tap `Quét`.
4. Say: "ARCore detects the physical lecture surface and locks the app to the board or projected slide."
5. Point out the subtle surface outline.
6. Tap `Dịch`.
7. Say: "The app captures the AR camera frame, sends it to the backend for OCR and translation, and maps the translated text back onto the detected surface."
8. Move the phone left or right.
9. Say: "The translations are not ordinary screen labels. They are placed in world space, so they remain attached to the real slide as the camera moves."
10. Tap `Xóa`.
11. Say: "The scene resets cleanly and is ready for another slide."

## Fallback Path

- If Google API credentials or network latency are unstable, enable backend mock mode only as an explicit fallback.
- Use the same flow and explain that fallback mode is deterministic but does not read the current slide.

## Talking Points

- Surface lock: the app understands a real board/slide instead of drawing generic UI.
- World-space labels: translations are anchored to AR positions.
- BBox-aware placement: labels map from OCR boxes back onto the detected document surface instead of floating as generic screen UI.
- Stable anchors: moving the device keeps labels attached to the surface.
- Backend pipeline: OCR, formula-safe translation, document surface estimation, and latency reporting all run through the Flask backend.
