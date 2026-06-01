# AR Lecture Assistant Testing Checklist

## Baseline Demo Mode

- Open `Assets/Scenes/MainScene.unity`.
- Confirm the default scene contains AR Session, XR Origin, Main Camera, ARPlaneManager, ARRaycastManager, and ARAnchorManager.
- Press Play in Unity Editor.
- Confirm the default demo UI shows only the primary button (`Quét` / `Dịch` / `Dịch lại` / `Thử lại`) and `Xóa`.
- Confirm debug, freeze, transcript, provider, and `Hide VN` controls are hidden unless their serialized optional flags are enabled.
- Confirm no button text overflows in landscape orientation.

## Backend Mock

- Start the backend with `python backend/app.py`.
- Run `python scripts/post_sample_frame.py --image samples/slides/slide_01.png --mock`.
- Confirm the response includes `blocks`, `document_surface`, and `latency_ms.surface_detection`.
- Confirm backend logs include pipeline latency; set `PIPELINE_SLOW_MS=0` to force the slow-pipeline warning path during local verification.
- Confirm mock mode returns deterministic OCR/translation blocks.

## Android AR Device

- Build to an ARCore-compatible Android device.
- Cold start the app with the backend reachable on the same LAN.
- Point the camera at a board or projected slide in normal light.
- Tap `Quét`.
- Confirm a plane is detected within 3 seconds and the primary button changes to `Dịch`.
- Confirm a subtle AR surface outline appears after lock.
- Tap `Dịch`.
- Confirm translated labels appear on the physical surface.
- Move 0.5-1 meter left/right and tilt the phone.
- Confirm labels and the surface outline remain attached to the board/slide.
- Tap `Xóa`.
- Confirm all labels and outline clear, and the app returns to the ready state.

## Reliability Runs

- Repeat the full scan -> translate -> move -> clear flow five times without restarting.
- Repeat once in low light.
- Repeat once with the slide angled around 30 degrees.
- Repeat once with the backend offline and confirm the app shows a short retryable error.

## Regression Checks

- Run `python -m pytest backend/tests -q` from the repo root or `python -m pytest tests -q` from `backend/`.
- Run `git diff --check`.
- In real OCR mode, verify Tesseract tests skip cleanly if the binary is unavailable.
