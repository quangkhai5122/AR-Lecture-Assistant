# AR Lecture Assistant Demo Script

## Setup

- Android ARCore device in landscape orientation.
- Backend running on the demo laptop and reachable from the phone over LAN.
- Unity build configured with mock mode available as fallback.
- Slide or board visible with high contrast text.

## Two-Minute Flow

1. Open the app and point the camera at the lecture slide.
2. Say: "The app starts with a clean AR camera view and only the controls needed for the demo."
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

- If real OCR or network latency is unstable, enable backend mock mode.
- Use the same flow and explain that mock mode keeps the demo deterministic while preserving the AR surface lock and world-space placement behavior.

## Talking Points

- Surface lock: the app understands a real board/slide instead of drawing generic UI.
- World-space labels: translations are anchored to AR positions.
- Stable anchors: moving the device keeps labels attached to the surface.
- Backend pipeline: OCR, formula-safe translation, document surface estimation, and latency reporting all run through the Flask backend.
