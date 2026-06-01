# AR Upgrade Implementation Status

This file tracks current progress against `AR_UPGRADE_MASTER_PLAN.md`.

## Done With Automated Evidence

### Sprint 0.1 - Baseline Audit

- `docs/TESTING_CHECKLIST.md` exists.
- `README.md` documents baseline demo mode.
- Backend mock pipeline is covered by tests.

Evidence:

- `python -m pytest tests -q` from `backend/`
- `git diff --check`

### Sprint 1.1 - AR Camera Raw First

- `FrameCaptureService` defaults to `FrameCaptureSource.Auto`.
- AR CPU image is attempted before screenshot fallback.
- Capture source and fallback warning are recorded for debug verification.
- Screenshot fallback masks screen-space UI by default.

Remaining manual evidence:

- Android device log confirming `ar_camera_raw` on an ARCore device.

### Sprint 1.2 - Robust Tracking State Controller

- `ARSurfaceLockController` added.
- Tracking states exist: `SearchingPlane`, `PlaneFound`, `SurfaceLocked`, `TrackingLimited`, `Lost`.
- Surface lock event updates app state through `UIManager`.
- Plane detection is disabled after lock by default.

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
- OCR bboxes from the warped surface are mapped back to original image coordinates.
- If surface OCR returns no blocks, pipeline falls back to original-frame OCR.

Evidence:

- `test_document_surface_crop_uses_quadrilateral_warp_mapping`
- `test_pipeline_runs_real_ocr_on_surface_crop`

### Sprint 3.1 / 3.2 - Surface Mapping and World Labels

- `ARDocumentSurface` and `ARDocumentSurfaceMapper` added.
- `ARLabelPlacer` maps image-space OCR points onto AR plane surface corners when available.
- Existing world-space labels remain the default.

Remaining manual evidence:

- Device test proving label projection error is acceptable on real slides.
- Device movement test proving labels remain stable.

### Sprint 4.1 / 4.2 - Visual AR Feedback

- `ARSurfaceOutlineRenderer` added.
- Surface outline renders from locked pose or mapped document corners.
- `ARLabelRevealAnimator` added and applied to world-space labels.

Remaining manual evidence:

- Visual check that outline is aligned and not distracting.
- Frame-rate check on Android demo device.

### Sprint 5 / 7 - Backend Hardening

- OCR preprocessing already includes grayscale, autocontrast, contrast, sharpness, unsharp mask, line merging, and provider fallback.
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

- Two-button demo mode defaults are implemented by serialized flags and polish logic.
- Transcript and `Hide VN` remain available as optional controls, hidden by default for demo.
- Friendly user-facing error messages are in `ButtonController`.

Manual checks still required:

- Open `Assets/Scenes/MainScene.unity` in Unity.
- Press Play and confirm only required demo controls are visible.
- Build to Android and run the full checklist in `docs/TESTING_CHECKLIST.md`.

## Not Yet Fully Proven

- App runs on an Android ARCore device.
- Surface outline aligns with a real board/slide across movement.
- Translation labels remain stable while moving 0.5-1 meter sideways.
- Real OCR works on at least 4/5 prepared real-world sample slides.
- Five consecutive full demos complete without restart.

These require physical-device verification and cannot be marked complete from repository tests alone.
