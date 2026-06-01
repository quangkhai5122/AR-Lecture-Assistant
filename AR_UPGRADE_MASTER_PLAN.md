# AR Lecture Assistant - Master Upgrade Plan

## Purpose

This document is the implementation plan for upgrading AR Lecture Assistant into a polished, high-impact AR demo. It is written for AI coding agents and human contributors. Each sprint includes scope, target files, expected output, acceptance criteria, and test requirements.

The target is not only to make the app work. The target is to make the AR nature obvious within the first 10 seconds of the demo:

- The app understands the physical lecture surface.
- The app locks onto a real board or slide.
- Translations appear on that real surface, not as generic screen UI.
- Labels stay stable when the user moves the phone.
- The user flow remains simple: five visible controls, clear status, no debug clutter.

## Current Technical Baseline

### Strengths

- Unity project exists at the repository root with `Assets/`, `Packages/`, and `ProjectSettings/`.
- AR packages are already installed:
  - `com.unity.xr.arfoundation`
  - `com.unity.xr.arcore`
- Main scene already contains core AR objects:
  - `AR Session`
  - `XR Origin`
  - `Main Camera`
  - `ARPlaneManager`
  - `ARRaycastManager`
  - `ARAnchorManager`
- Existing Unity scripts already cover:
  - AR raycast: `Assets/Scripts/AR/ARRaycastController.cs`
  - anchor placement: `Assets/Scripts/AR/ARAnchorPlacer.cs`
  - translation label placement: `Assets/Scripts/AR/ARLabelPlacer.cs`
  - frame capture: `Assets/Scripts/Services/FrameCaptureService.cs`
  - UI flow: `Assets/Scripts/UI/ButtonController.cs`
- Backend already supports:
  - `/pipeline/frame`
  - `/ocr`
  - `/translate`
  - speech-related endpoints
  - mock and real OCR/translation providers
- Contracts already exist in `contracts/`.

### Weaknesses Blocking a 10/10 AR Demo

- The app can still feel like a camera overlay because visual AR feedback is limited.
- Surface detection is not strong enough. Backend estimates `document_surface` from OCR bounding boxes instead of detecting the actual slide/board quadrilateral.
- Anchor creation is minimal and should be upgraded to attach to AR trackables when possible.
- Mapping from OCR bbox to AR world surface needs stronger validation and visual polish.
- Capture currently supports AR camera raw, but default behavior and demo path must make this reliable.
- Advanced debug controls should remain hidden in demo mode, while transcript and translation visibility stay available.
- There is no structured demo acceptance checklist.

## Target 10/10 Experience

The final demo should follow this flow:

1. User opens the app.
2. The app shows the default five-button demo UI: `Hide VN`, `Transcript`, `Quét`, `Dịch`, and `Xóa`.
3. User points the phone at a board or projected slide.
4. User taps `Quét`.
5. The app detects a plane and draws a subtle AR outline around the board/slide surface.
6. User taps `Dịch`.
7. The app captures an AR camera frame, sends it to backend OCR/translation, and maps the result back to the detected surface.
8. Translated labels animate into place directly on the board/slide.
9. User can tap `Hide VN` to hide translations and tap again to show them.
10. User moves left/right or tilts the phone. Labels remain attached to the physical surface.
11. User taps `Xóa`. The scene resets cleanly.

## Success Metrics

Use these metrics as the final scoring rubric.

| Area | Target Score | Required Result |
|---|---:|---|
| AR plane and surface lock | 10/10 | Board/slide lock is visible and stable. |
| Spatial label placement | 10/10 | Labels stay attached while the camera moves. |
| Backend OCR/translation | 9/10 | Reads key slide text accurately enough for demo. |
| UI/UX | 10/10 | Only essential controls, no debug clutter. |
| Performance | 8.5/10+ | Smooth enough on Android demo device. |
| Reliability | 9/10 | Five consecutive demos without crash. |

## Agent Rules

All implementation agents should follow these rules:

- Keep demo mode simple. The default visible UI must remain the five core controls: `Hide VN`, `Transcript`, `Quét`, `Dịch`, and `Xóa`.
- Do not remove backend mock mode. It is necessary for deterministic demos.
- Prefer adding small focused components over rewriting all scene logic.
- Keep runtime debug tools behind serialized flags or inactive scene objects.
- Add tests for backend changes.
- For Unity behavior that cannot be covered by automated tests, add a manual acceptance checklist.
- Do not make unrelated refactors.
- Do not modify `client-unity/` unless the change must keep the snapshot aligned.

## Proposed Workstreams

### Workstream A - Unity AR Core

Goal: make the project visually and technically feel like a real AR app.

Primary files:

- `Assets/Scenes/MainScene.unity`
- `Assets/Scripts/AR/ARRaycastController.cs`
- `Assets/Scripts/AR/ARAnchorPlacer.cs`
- `Assets/Scripts/AR/ARLabelPlacer.cs`
- `Assets/Scripts/Services/FrameCaptureService.cs`
- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/StateManager.cs`

### Workstream B - Backend Vision Pipeline

Goal: detect the slide or board surface, not only text boxes.

Primary files:

- `backend/services/pipeline_service.py`
- `backend/services/ocr_service.py`
- new file: `backend/services/document_surface_service.py`
- `backend/tests/test_pipeline.py`
- contracts in `contracts/`

### Workstream C - UI/UX and Demo Polish

Goal: keep the interface simple while making the AR state obvious.

Primary files:

- `Assets/Scripts/UI/ARLectureVisualPolish.cs`
- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/StateManager.cs`
- `Assets/Prefabs/FixedARLabel.prefab`
- `Assets/Prefabs/SubtitlePanel.prefab`

### Workstream D - QA, Demo Script, and Documentation

Goal: make the final demo repeatable and easy to grade.

Primary files:

- this plan
- `README.md`
- new file: `docs/DEMO_SCRIPT.md`
- new file: `docs/TESTING_CHECKLIST.md`
- sample assets in `samples/`

## Phase 0 - Stabilize Baseline and Demo Mode

Objective: create a clean baseline before deep AR changes.

Duration: 1 sprint.

### Sprint 0.1 - Baseline Audit

Tasks:

- Confirm the default scene is `Assets/Scenes/MainScene.unity`.
- Confirm demo UI shows `Hide VN`, `Transcript`, `Quét`, `Dịch`, and `Xóa`.
- Confirm debug and freeze controls are hidden in the default demo mode.
- Confirm backend can run in mock mode.
- Confirm Android build settings target ARCore-compatible devices.

Target files:

- `Assets/Scenes/MainScene.unity`
- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/ARLectureVisualPolish.cs`
- `README.md`

Output:

- `docs/TESTING_CHECKLIST.md` with baseline checks.
- A short baseline section in `README.md`.

Acceptance criteria:

- Opening the main scene in Unity shows the five required demo controls.
- No debug panel is visible by default.
- Backend mock request returns pipeline blocks.
- The project opens without missing script references.

Commands:

```powershell
Push-Location backend
..\.venv\Scripts\python.exe -m pytest tests -q
Pop-Location
git diff --check
```

Manual checks:

- Open `Assets/Scenes/MainScene.unity`.
- Press Play in Unity Editor.
- Verify no debug-only UI is visible.

Risks:

- The local `.venv` may miss dependencies. If tests fail with missing modules, run:

```powershell
.\.venv\Scripts\python.exe -m pip install -r backend\requirements.txt
```

## Phase 1 - AR Camera and Tracking Foundation

Objective: make the app capture real AR camera data and track surfaces reliably.

Duration: 1-2 sprints.

### Sprint 1.1 - AR Camera Raw First

Tasks:

- Change `FrameCaptureService` demo default to prefer `FrameCaptureSource.Auto` or `ARCameraRaw`.
- Keep screenshot fallback for Editor and unsupported devices.
- Ensure UI is never included in OCR input when screenshot fallback is used.
- Add clear warning status when AR CPU image is unavailable.

Target files:

- `Assets/Scripts/Services/FrameCaptureService.cs`
- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/StateManager.cs`

Output:

- AR camera raw capture is preferred on Android.
- Screenshot fallback remains stable.
- User-facing status remains short and friendly.

Acceptance criteria:

- On Android, OCR capture uses `ARCameraManager.TryAcquireLatestCpuImage` when available.
- If raw image capture fails, app falls back without crashing.
- Captured frame does not contain app UI controls.

Manual test:

- Build to Android.
- Point camera at a slide.
- Tap `Quét`, then `Dịch`.
- Check backend logs or debug log for capture source.

### Sprint 1.2 - Robust Tracking State Controller

Tasks:

- Add `Assets/Scripts/AR/ARSurfaceLockController.cs`.
- Track states:
  - `SearchingPlane`
  - `PlaneFound`
  - `SurfaceLocked`
  - `TrackingLimited`
  - `Lost`
- Centralize plane lock/unlock behavior.
- Expose events for UI status and visual outline.

Target files:

- new file: `Assets/Scripts/AR/ARSurfaceLockController.cs`
- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/StateManager.cs`
- `Assets/Scenes/MainScene.unity`

Output:

- A single controller owns AR tracking state.
- UI no longer guesses AR state from scattered callbacks.

Acceptance criteria:

- App detects a plane within 3 seconds in normal light.
- If tracking is lost, status changes within 1 second.
- Plane detection is disabled after surface lock to reduce visual noise.

Manual test:

- Start app.
- Move camera over table/wall/board.
- Observe state transitions in status text.
- Cover camera briefly and verify tracking warning.

## Phase 2 - Real Document Surface Detection

Objective: detect the physical board/slide surface as a quadrilateral.

Duration: 2 sprints.

### Sprint 2.1 - Backend Document Surface Detector

Tasks:

- Add `DocumentSurfaceService`.
- Use image preprocessing:
  - grayscale
  - blur
  - edge detection
  - contour detection
  - quadrilateral approximation
  - confidence scoring
- Return `document_surface` with:
  - `corners`
  - `confidence`
  - `method`
  - `source`
- Keep existing OCR bbox union as fallback.

Target files:

- new file: `backend/services/document_surface_service.py`
- `backend/services/pipeline_service.py`
- `backend/tests/test_pipeline.py`
- `contracts/pipeline_response.schema.json`
- `contracts/sample_pipeline_output.json`

Output:

- Backend returns a better surface estimate when slide/board edges are visible.
- Fallback behavior remains available.

Acceptance criteria:

- For 5 sample slide/board images, at least 4 return non-null `document_surface`.
- Surface confidence is higher for contour-detected surfaces than OCR-union fallback.
- If no surface is detected, pipeline still returns OCR blocks.

Test requirements:

- Add tests for:
  - contour-based surface detection
  - fallback to OCR bbox union
  - invalid image handling
  - confidence range

Suggested test names:

- `test_document_surface_detects_quadrilateral`
- `test_document_surface_falls_back_to_ocr_union`
- `test_document_surface_returns_none_for_blank_image`

### Sprint 2.2 - Surface Cropping and OCR Improvement

Tasks:

- When `document_surface` is detected, crop/warp the surface before OCR.
- Run OCR on the normalized board/slide crop.
- Map OCR bboxes back to original image coordinates.
- Preserve the original response contract.

Target files:

- `backend/services/document_surface_service.py`
- `backend/services/ocr_service.py`
- `backend/services/pipeline_service.py`
- `backend/tests/test_pipeline.py`

Output:

- Better OCR for angled slides.
- More accurate bboxes.

Acceptance criteria:

- OCR bboxes still map to original image dimensions.
- OCR improves or stays equal on angled test sample.
- No regression in mock mode.

Manual test:

- Capture a projected slide at a 30-degree angle.
- Compare OCR before and after surface crop.

## Phase 3 - Spatial Mapping and World-Space Labels

Objective: make translated text stick to the real surface correctly.

Duration: 2 sprints.

### Sprint 3.1 - Surface Corner Raycast and Lock

Tasks:

- In Unity, project backend `document_surface.corners` into screen points.
- Raycast each corner onto the locked AR plane.
- Build an AR document surface model:
  - image-space corners
  - screen-space corners
  - world-space corners
  - plane pose
  - confidence
- Cache this model for label placement.

Target files:

- `Assets/Scripts/AR/ARLabelPlacer.cs`
- new file: `Assets/Scripts/AR/ARDocumentSurface.cs`
- new file: `Assets/Scripts/AR/ARDocumentSurfaceMapper.cs`

Output:

- A reusable surface mapper for bbox-to-world conversion.

Acceptance criteria:

- Four surface corners can be visualized in AR.
- If corner raycast fails, app falls back gracefully.
- The mapper rejects low-confidence or invalid surfaces.

Manual test:

- Point at slide.
- Tap `Quét`.
- Confirm surface outline matches slide.
- Move camera and verify outline stays on the slide plane.

### Sprint 3.2 - OCR BBox to AR World Label Placement

Tasks:

- Convert each OCR bbox center into document UV.
- Convert UV into world position on the AR surface.
- Place each label as a child of an AR anchor or surface root.
- Scale labels by surface size and camera distance.
- Avoid label overlap with a deterministic layout rule.

Target files:

- `Assets/Scripts/AR/ARDocumentSurfaceMapper.cs`
- `Assets/Scripts/AR/ARLabelPlacer.cs`
- `Assets/Prefabs/FixedARLabel.prefab`

Output:

- Translation labels appear on the corresponding slide text areas.

Acceptance criteria:

- Label center error is less than 10% of the corresponding OCR bbox width/height in screen projection.
- Labels remain attached when moving 0.5-1 meter sideways.
- Labels do not grow/shrink abruptly while camera moves.

Manual test:

- Use 5 known slide images.
- For each slide, capture and place labels.
- Move phone left, right, closer, farther.
- Verify labels remain stable.

## Phase 4 - AR Visual Wow Layer

Objective: make the AR behavior obvious and polished.

Duration: 1-2 sprints.

### Sprint 4.1 - Surface Outline and Lock Feedback

Tasks:

- Add an AR outline object for the detected board/slide.
- Draw subtle corner brackets or a thin border on the locked surface.
- Animate border from scanning to locked state.
- Keep color palette black/white/minimal.

Target files:

- new file: `Assets/Scripts/AR/ARSurfaceOutlineRenderer.cs`
- `Assets/Scenes/MainScene.unity`
- optional prefab: `Assets/Prefabs/ARSurfaceOutline.prefab`

Output:

- Viewer immediately sees the detected surface.

Acceptance criteria:

- Surface outline appears only after plane/surface lock.
- Outline follows the surface while camera moves.
- Outline does not cover slide content too aggressively.

Manual test:

- Scan a board.
- Confirm outline aligns with board/slide edges.

### Sprint 4.2 - Label Reveal Animation

Tasks:

- Add simple fade/scale animation for labels.
- Sequence labels top-to-bottom or by OCR order.
- Avoid distracting motion.
- Keep animation under 600 ms total for most slides.

Target files:

- new file: `Assets/Scripts/UI/ARLabelRevealAnimator.cs`
- `Assets/Scripts/AR/ARLabelPlacer.cs`
- `Assets/Prefabs/FixedARLabel.prefab`

Output:

- Translations appear with a clear "placed into AR" feel.

Acceptance criteria:

- Animation does not cause layout shifting after completion.
- Labels are readable during and after animation.
- No frame drops below acceptable demo threshold.

Manual test:

- Translate a slide with at least 3 text blocks.
- Confirm label reveal is smooth and not noisy.

### Sprint 4.3 - Optional Tap-to-Focus Label

Tasks:

- Allow tapping a translated label to focus it.
- Show one larger readable panel near the surface or bottom of screen.
- Keep this off by default if it adds UI clutter.

Target files:

- `Assets/Scripts/AR/ARLabelPlacer.cs`
- new file: `Assets/Scripts/UI/FocusedTranslationPanel.cs`

Output:

- Optional deeper interaction for demo questions.

Acceptance criteria:

- Tapping a label is reliable.
- Focus panel can be dismissed.
- Default demo still works without using this feature.

## Phase 5 - Backend Quality and Latency

Objective: make real OCR/translation demo reliable enough.

Duration: 2 sprints.

### Sprint 5.1 - OCR Preprocessing Upgrade

Tasks:

- Improve OCR preprocessing:
  - resize policy
  - contrast enhancement
  - sharpen/unsharp mask
  - thresholding option
  - crop surface before OCR when available
- Keep provider fallback behavior.
- Add warnings for low-confidence OCR.

Target files:

- `backend/services/ocr_service.py`
- `backend/services/document_surface_service.py`
- `backend/tests/test_pipeline.py`

Output:

- More stable OCR on classroom-like slides.

Acceptance criteria:

- Test sample OCR returns readable blocks.
- Low confidence blocks are filtered or flagged.
- Existing OCR tests pass.

### Sprint 5.2 - Translation and Formula Stability

Tasks:

- Ensure formulas are preserved.
- Merge same-line OCR blocks before translation.
- Cache repeated translations.
- Provide deterministic mock translation for demo.

Target files:

- `backend/services/formula_service.py`
- `backend/services/translation_service.py`
- `backend/services/pipeline_service.py`
- `backend/tests/test_pipeline.py`

Output:

- Translation feels stable and classroom-ready.

Acceptance criteria:

- Formula tests pass.
- Same input translation returns cached result.
- Mock mode is deterministic.

### Sprint 5.3 - Performance Budget

Tasks:

- Add latency fields for:
  - surface detection
  - OCR
  - translation
  - total pipeline
- Optimize pipeline for demo path.
- Add backend logging for slow stages.

Target files:

- `backend/services/pipeline_service.py`
- `backend/app.py`
- `backend/tests/test_pipeline.py`

Output:

- Clear latency reporting.

Acceptance criteria:

- Mock pipeline under 1 second.
- Real OCR + mock translation target under 4 seconds on demo laptop.
- Real OCR + real translation target under 6 seconds if network is stable.

## Phase 6 - UI/UX Final Demo Polish

Objective: make the user flow obvious and teacher-friendly.

Duration: 1 sprint.

### Sprint 6.1 - Five-Button Demo Flow

Tasks:

- Preserve five visible controls:
  - `Hide VN` / `Show VN`
  - `Transcript`
  - `Quét`
  - `Dịch` / `Dịch lại` / `Thử lại`
  - `Xóa`
- Hide debug, freeze, and provider controls from the default screen.
- Ensure no text overflows buttons.

Target files:

- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/ARLectureVisualPolish.cs`
- `Assets/Scripts/UI/StateManager.cs`
- `Assets/Scenes/MainScene.unity`

Output:

- Simple and confident interface.

Acceptance criteria:

- Exactly five core controls are visible in demo mode.
- User can complete demo with no explanation.
- Status messages are short and friendly.

### Sprint 6.2 - Human-Friendly Error States

Tasks:

- Replace stack traces with friendly messages in demo mode.
- Keep detailed errors in logs only.
- Add retry path through `Thử lại`.

Target files:

- `Assets/Scripts/UI/ButtonController.cs`
- `Assets/Scripts/UI/StateManager.cs`
- `Assets/Scripts/Services/HttpPipelineClient.cs`

Output:

- Demo does not expose backend/internal errors.

Acceptance criteria:

- Backend offline shows a short user-facing message.
- OCR failure shows actionable instruction.
- App can recover without restart.

## Phase 7 - End-to-End Testing and Demo Hardening

Objective: make the demo repeatable under classroom pressure.

Duration: 1 sprint.

### Sprint 7.1 - Automated Backend Test Suite

Tasks:

- Ensure backend tests pass in a clean environment.
- Add tests for document surface detection.
- Add tests for latency fields.
- Add contract tests for response shape.

Target files:

- `backend/tests/test_pipeline.py`
- `contracts/*.schema.json`

Output:

- Backend confidence before demo.

Acceptance criteria:

- `pytest` passes.
- Real OCR tests skip cleanly if Tesseract is unavailable.
- Mock pipeline always passes.

Command:

```powershell
Push-Location backend
..\.venv\Scripts\python.exe -m pytest tests -q
Pop-Location
```

### Sprint 7.2 - Unity Device Test Checklist

Tasks:

- Create a manual Android checklist.
- Include repeatability test.
- Include bad lighting test.
- Include backend offline test.

Target files:

- new file: `docs/TESTING_CHECKLIST.md`

Output:

- Test checklist any agent or teammate can follow.

Acceptance criteria:

- Checklist covers:
  - cold start
  - scan
  - lock
  - translate
  - move camera
  - clear
  - retry
  - offline backend
  - low light
  - angled slide

### Sprint 7.3 - Final Demo Script

Tasks:

- Write a 2-minute demo script.
- Include exact device setup.
- Include fallback path using mock mode.
- Include talking points that emphasize AR.

Target files:

- new file: `docs/DEMO_SCRIPT.md`
- `README.md`

Output:

- Presenter can demo confidently.

Acceptance criteria:

- Script fits within 2 minutes.
- It names the AR features clearly:
  - surface lock
  - world-space labels
  - stable anchors
  - real-time camera capture

## Implementation Backlog by Priority

### P0 - Must Have for Wow Demo

- Prefer AR camera raw capture.
- Add surface lock controller.
- Add AR surface outline.
- Detect document surface corners better.
- Map OCR bbox to AR world surface.
- Use stable anchors attached to AR tracking where possible.
- Keep only the five visible core controls on the default screen.
- Add demo checklist and demo script.

### P1 - Should Have

- Label reveal animation.
- Better OCR preprocessing.
- Friendly error recovery.
- Latency reporting.
- Backend tests for document surface.
- Sample slide image set.

### P2 - Nice to Have

- Tap-to-focus translation.
- Speech transcript as a default visible control with its deeper panels still optional.
- Persist anchors across short tracking interruptions.
- Light estimation styling.
- Occlusion/depth experiments.

## Detailed Acceptance Matrix

| Feature | Acceptance Test | Pass Condition |
|---|---|---|
| Plane scan | Android manual test | Plane found within 3 seconds in normal light. |
| Surface lock | Visual inspection | Outline matches board/slide surface. |
| Capture | Backend/log inspection | AR camera raw is used when available. |
| OCR | Backend tests + sample slide | Key text blocks returned. |
| Translation | Backend tests | Text translated and formulas preserved. |
| World labels | Device movement test | Labels remain on slide while moving. |
| UI | Visual inspection | Exactly five visible core controls: `Hide VN`, `Transcript`, `Quét`, `Dịch`, and `Xóa`. |
| Error handling | Backend offline test | Friendly retry message, no crash. |
| Performance | Timed demo | Mock under 1s, real under target budget. |
| Repeatability | Five-run test | Five consecutive successful demos. |

## Suggested Agent Task Decomposition

### Agent 1 - Unity AR Foundation

Scope:

- `ARSurfaceLockController`
- anchor upgrade
- surface outline
- scene wiring

Do not touch:

- backend OCR internals
- translation logic

### Agent 2 - Backend Vision

Scope:

- `DocumentSurfaceService`
- pipeline response changes
- backend tests
- contracts

Do not touch:

- Unity scene layout unless contract field names change

### Agent 3 - Spatial Mapping

Scope:

- `ARDocumentSurface`
- `ARDocumentSurfaceMapper`
- bbox to world-space labels
- label scaling and overlap

Do not touch:

- backend provider setup
- UI visual theme except necessary label hooks

### Agent 4 - UX and Demo Polish

Scope:

- five-button flow
- status messages
- friendly errors
- demo script
- testing checklist

Do not touch:

- core OCR provider code

## Definition of Done for the Whole Upgrade

The upgrade is complete only when all of the following are true:

- App runs on Android ARCore device.
- Main demo uses only five visible core controls.
- User can lock onto a slide/board.
- Surface outline appears in AR.
- Translation labels appear on the physical surface.
- Labels remain stable during camera movement.
- Backend mock mode works for deterministic demo.
- Real OCR mode works on at least 4/5 prepared sample slides.
- Backend tests pass.
- `docs/DEMO_SCRIPT.md` and `docs/TESTING_CHECKLIST.md` exist.
- The app can be demoed five times consecutively without restart.

## Final Demo Grading Narrative

Use this explanation during presentation:

> This is not just a translation overlay. The app first uses ARCore and ARFoundation to understand the physical lecture surface. It locks onto the board or slide, estimates the document surface, captures the camera frame for OCR and translation, then maps each translated text block back into world space. When the phone moves, the translations remain attached to the real board.

## Recommended Sprint Order

1. Sprint 0.1 - Baseline audit.
2. Sprint 1.1 - AR camera raw first.
3. Sprint 1.2 - tracking state controller.
4. Sprint 4.1 - surface outline early for visible demo value.
5. Sprint 2.1 - backend document surface detector.
6. Sprint 3.1 - surface corner raycast and lock.
7. Sprint 3.2 - bbox to world-space labels.
8. Sprint 4.2 - label reveal animation.
9. Sprint 5.1 - OCR preprocessing.
10. Sprint 6.2 - friendly error states.
11. Sprint 7.1 - backend tests.
12. Sprint 7.2 and 7.3 - checklist and demo script.

## Notes for Future Agents

- If short on time, prioritize visible AR proof over backend sophistication.
- The strongest "wow" moment is: outline locks onto the board, then translated labels appear on that surface and stay there while the phone moves.
- Avoid adding more default buttons. Extra features should stay behind inspector flags or debug mode.
- Keep mock mode excellent. A controlled demo is better than an unstable real-provider demo.
- Record a successful screen capture after each major phase. This protects the project if live network or OCR fails during presentation.
