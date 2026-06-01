# AR Upgrade Implementation Status

This file tracks current progress against `AR_UPGRADE_MASTER_PLAN.md`.

## Done With Automated Evidence

### Latest Verification

- `python -m pytest tests -q` from `backend/`: 41 passed, 2 skipped.
- Flask test-client smoke request to `/pipeline/frame` in mock mode returned HTTP 200, 3 blocks, `document_surface.method=contour_quadrilateral`, and `mock_used=true`.
- `git diff --check` passed.
- `Assets/Plugins/Android/AndroidManifest.xml` parses as valid XML.
- Unity 2022.3.62f3 batchmode opened the project, refreshed assets, compiled `Assembly-CSharp`, and exited successfully with return code 0 (`unity-compile.log`).
- Roslyn fallback compile over all `Assets/**/*.cs` passed without warnings.
- `Assets/Editor/AndroidBuild.cs` provides a repeatable `AndroidBuild.BuildApk` batchmode APK build method.
- `Assets/Scenes/MainScene.unity` scene file IDs are unique, and the explicit AR/backend component references are present.
- `ProjectSettings/EditorBuildSettings.asset` points at `Assets/Scenes/MainScene.unity`.
- `Packages/manifest.json` includes `com.unity.xr.arcore` and `com.unity.xr.arfoundation`.
- `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` enables the ARCore loader for Android.
- `Assets/Plugins/Android/AndroidManifest.xml` declares camera permission, `android.hardware.camera.ar` as required, and `android:exported="true"` for the launcher activity.
- `Assets/Scenes/MainScene.unity` explicitly wires `FrameCaptureService`, `HttpPipelineClient`, `ARSurfaceOutlineRenderer`, and `ARSurfaceLockController` so demo-critical settings are visible in the Inspector before Android builds.
- `docs/AR_UPGRADE_ACCEPTANCE_AUDIT.md` maps each final Definition of Done item to current evidence or missing device evidence.

### Sprint 0.1 - Baseline Audit

- `docs/TESTING_CHECKLIST.md` exists.
- `docs/AR_UPGRADE_ACCEPTANCE_AUDIT.md` exists.
- `README.md` documents baseline demo mode.
- Backend mock pipeline is covered by tests.

Evidence:

- `python -m pytest tests -q` from `backend/`
- `git diff --check`

### Sprint 1.1 - AR Camera Raw First

- `FrameCaptureService` defaults to `FrameCaptureSource.Auto`.
- `FrameCaptureService` is now attached in `MainScene` with a direct `ARCameraManager` reference instead of depending only on runtime auto-add.
- AR CPU image is attempted before screenshot fallback.
- Capture source and fallback warning are recorded for debug verification, capture source is logged for Android logcat evidence, and screenshot fallback warning is surfaced through the demo status text when debug UI is hidden.
- Screenshot fallback masks screen-space UI by default.

Remaining manual evidence:

- Android device log confirming `ar_camera_raw` on an ARCore device.

### Sprint 1.2 - Robust Tracking State Controller

- `ARSurfaceLockController` added.
- `ARSurfaceLockController` and `ARSurfaceOutlineRenderer` are attached to `XR Origin` in the main scene and referenced by `UIManager`, `ButtonController`, and `ARLabelPlacer`.
- Tracking states exist: `SearchingPlane`, `PlaneFound`, `SurfaceLocked`, `TrackingLimited`, `Lost`.
- Surface lock event updates app state through `UIManager`.
- `UIManager` and `ButtonController` route scan, clear, and advanced freeze actions through `ARSurfaceLockController`; direct `ARPlaneManager` toggles are fallback-only.
- Plane detection is disabled after lock by default.
- Short ARSession tracking interruptions now move a locked surface to `TrackingLimited` and recover to `SurfaceLocked` when `SessionTracking` returns, instead of permanently dropping to `Lost`.

Remaining manual evidence:

- Android device test proving plane lock within 3 seconds in normal light.
- Tracking lost/limited behavior verified on device.

### Sprint 2.1 - Backend Document Surface Detector

- `DocumentSurfaceService` added.
- Detector returns `document_surface` with `corners`, `confidence`, `method`, and `source`.
- OCR bbox union fallback remains available.
- Tests cover quadrilateral detection, fallback, blank image, and 4/5 sample slide detection.

Evidence:

- `test_document_surface_detects_quadrilateral`
- `test_document_surface_falls_back_to_ocr_union`
- `test_document_surface_returns_none_for_blank_image`
- `test_document_surface_detects_four_of_five_sample_slides`

### Sprint 2.2 - Surface Cropping and OCR Improvement

- Detected document surface is perspective-warped before OCR in real mode.
- OCR bboxes from the warped surface are mapped back to original image coordinates using the crop's perspective transform.
- If surface OCR returns no blocks, pipeline falls back to original-frame OCR.

Evidence:

- `test_document_surface_crop_uses_quadrilateral_warp_mapping`
- `test_document_surface_crop_maps_points_with_perspective_transform`
- `test_pipeline_runs_real_ocr_on_surface_crop`

### Sprint 3.1 / 3.2 - Surface Mapping and World Labels

- `ARDocumentSurface` and `ARDocumentSurfaceMapper` added.
- `ARDocumentSurface` stores image-space corners, screen-space corners, world-space corners, a plane pose, and the common hit `ARPlane` when one is available.
- `ARDocumentSurfaceMapper` precomputes the image-to-surface homography, rejects low-area/invalid surfaces, and can project missed corner raycasts onto the cached locked plane before falling back.
- `ARLabelPlacer` maps OCR bbox centers onto the detected AR document surface when available, with overlap nudges capped to 8% of the source bbox size so labels stay tied to the detected text.
- `ARAnchorPlacer` now attempts to attach anchors to the hit or mapped `ARPlane` before falling back to standalone anchors.
- `ARSurfaceLockController` keeps the locked `ARPlane` active with visuals hidden after lock, so mapped labels can still attach to the tracked plane while plane detection visuals stay out of the demo. Optional freeze/unfreeze also preserves the locked plane.
- Existing world-space labels remain the default.

Remaining manual evidence:

- Device test proving label projection error is acceptable on real slides.
- Device movement test proving labels remain stable.

### Sprint 4.1 / 4.2 - Visual AR Feedback

- `ARSurfaceOutlineRenderer` added.
- Surface outline renders from locked pose or mapped document corners.
- `ARLabelRevealAnimator` added and applied to world-space labels.
- `FocusedTranslationPanel` added for optional tap-to-focus label review; the flag remains off by default so full-feature UI does not open extra panels unless explicitly enabled.

Remaining manual evidence:

- Visual check that outline is aligned and not distracting.
- Frame-rate check on Android demo device.
- Optional tap-to-focus interaction verified on device if enabled.

### Sprint 5 / 7 - Backend Hardening

- OCR preprocessing already includes grayscale, autocontrast, contrast, sharpness, unsharp mask, optional binary thresholding through `OCR_THRESHOLD_ENABLED` / `OCR_THRESHOLD_VALUE`, line merging, and provider fallback.
- Latency fields include `surface_detection`, `ocr`, `translation`, and `total`.
- Backend logs slow pipeline runs using `PIPELINE_SLOW_MS`.
- Contract tests validate live and sample pipeline response shape.
- Mock pipeline latency budget is tested under 1 second.

Evidence:

- `test_pipeline_response_matches_contract_schema`
- `test_sample_pipeline_output_matches_contract_schema`
- `test_mock_pipeline_latency_budget_under_one_second`
- `test_pipeline_endpoint_logs_slow_latency`

## Done With Manual Checklist Pending

- Full-feature UI defaults are implemented by serialized flags and polish logic.
- `ButtonController` defaults to backend mock pipeline with mock translation for deterministic no-key demos.
- The main scene now defaults to the five visible controls `Hide VN`, `Transcript`, `Quét`, `Dịch`, and `Xóa`; `Freeze` and debug remain available in code behind `showAdvancedControls`, and compact two-button demo still exists through `useCompactDemoControls` and related flags.
- Friendly user-facing error messages are in `ButtonController`.
- `Thử lại` preserves the current locked surface for backend/OCR/placement errors and retries translation immediately when tracking is still locked; if tracking is not locked anymore, it starts a fresh scan.
- User-triggered clear now suppresses the tracking-lost warning path so the clear action returns directly to the ready state.

Manual checks still required:

- Open `Assets/Scenes/MainScene.unity` in Unity.
- Press Play and confirm the five required controls are visible.
- Build to Android and run the full checklist in `docs/TESTING_CHECKLIST.md`.
- Batch APK build was retried after a successful Unity compile, but this desktop session still stopped before Gradle because Unity Licensing Client IPC timed out with return code 199. Refresh Unity licensing via Hub/Editor, then rerun `AndroidBuild.BuildApk`.

## Not Yet Fully Proven

- App runs on an Android ARCore device.
- Surface outline aligns with a real board/slide across movement.
- Translation labels remain stable while moving 0.5-1 meter sideways.
- Real OCR works on at least 4/5 prepared real-world sample slides.
- Five consecutive full demos complete without restart.

These require physical-device verification and cannot be marked complete from repository tests alone.
