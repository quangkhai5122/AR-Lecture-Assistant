# AR Lecture Assistant Testing Checklist

## Full-Feature UI Baseline

- Open `Assets/Scenes/MainScene.unity`.
- Confirm the default scene contains AR Session, XR Origin, Main Camera, ARPlaneManager, ARRaycastManager, and ARAnchorManager.
- Confirm `UICanvas` has `FrameCaptureService` and `HttpPipelineClient`, and `XR Origin` has `ARSurfaceLockController` and `ARSurfaceOutlineRenderer`.
- Press Play in Unity Editor.
- Confirm the default UI shows exactly `Hide VN` / `Show VN`, `Transcript`, `Quét`, `Dịch`, and `Xóa`.
- Confirm `Freeze` and the debug toggle are hidden on the default screen.
- Confirm compact two-button demo is still available by enabling `useCompactDemoControls` and disabling transcript/advanced optional flags if a minimal presentation is needed.
- Confirm no button text overflows in landscape orientation.

## Backend Mock

- Start the backend with `python backend/app.py`.
- Run `python scripts/post_sample_frame.py --image samples/slides/slide_01.png --mock`.
- Confirm the response includes `blocks`, `document_surface`, and `latency_ms.surface_detection`.
- Confirm backend logs include pipeline latency; set `PIPELINE_SLOW_MS=0` to force the slow-pipeline warning path during local verification.
- Confirm mock mode returns deterministic OCR/translation blocks.

## Android AR Device

- Optional local APK build smoke:
  `Unity.exe -batchmode -quit -projectPath <repo> -buildTarget Android -executeMethod AndroidBuild.BuildApk -outputPath Builds\Android\ARLectureAssistant.apk`.
- Build to an ARCore-compatible Android device.
- Before building to device, set `HttpPipelineClient.backendBaseUrl` on `UICanvas` to the backend laptop LAN URL, for example `http://192.168.1.20:5000`.
- Cold start the app with the backend reachable on the same LAN.
- Point the camera at a board or projected slide in normal light.
- Tap `Quét`.
- Confirm a plane is detected within 3 seconds and the separate `Dịch` button becomes available.
- Confirm a subtle AR surface outline appears after lock.
- Tap `Dịch`.
- Confirm translated labels appear on the physical surface.
- Confirm each label stays close to the source text area; overlap nudges should not move a label more than a small fraction of its OCR box.
- Check Android logcat for `[FrameCaptureService] Captured frame via ar_camera_raw` in the normal device path; screenshot fallback should appear only when CPU image capture is unavailable.
- Move 0.5-1 meter left/right and tilt the phone.
- Confirm labels and the surface outline remain attached to the board/slide.
- Tap `Xóa`.
- Confirm all labels and outline clear, the app returns to the ready state, and no tracking-lost warning appears during the user-triggered clear.

## Optional Focus Interaction

- Enable `enableTapToFocusLabel` on `ARLabelPlacer`.
- Translate a slide with at least one label.
- Tap a translated label.
- Confirm a larger focused translation panel appears and can be dismissed with `X`.
- Disable the flag again unless the full demo needs tap-to-focus interaction.

## Reliability Runs

- Repeat the full scan -> translate -> move -> clear flow five times without restarting.
- Repeat once in low light.
- Repeat once with the slide angled around 30 degrees.
- Repeat once with the backend offline and confirm the app shows a short retryable error.
- Restore the backend and tap `Thử lại`; if the surface is still locked, confirm translation retries without forcing a new scan.

## Regression Checks

- Run `python -m pytest backend/tests -q` from the repo root or `python -m pytest tests -q` from `backend/`.
- Run `git diff --check`.
- In real OCR mode, verify Tesseract tests skip cleanly if the binary is unavailable.
