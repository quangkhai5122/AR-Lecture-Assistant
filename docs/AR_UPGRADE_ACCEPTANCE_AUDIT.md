# AR Upgrade Acceptance Audit

This audit maps the whole-upgrade Definition of Done in `AR_UPGRADE_MASTER_PLAN.md` to current evidence.

## Verified Locally

| Requirement | Current Evidence |
|---|---|
| Backend mock mode works for deterministic demo. | `python -m pytest tests -q` from `backend/` passed with 41 passed, 2 skipped. Flask test-client `/pipeline/frame` smoke returned HTTP 200, 3 blocks, `document_surface.method=contour_quadrilateral`, and `mock_used=true`. |
| Backend tests pass. | `python -m pytest tests -q` from `backend/`: 41 passed, 2 skipped. |
| `docs/DEMO_SCRIPT.md` and `docs/TESTING_CHECKLIST.md` exist. | Both files exist and include the two-minute demo flow plus Android AR device, repeatability, offline backend, low-light, and angled-slide checks. |
| Main scene is configured as the build scene. | `ProjectSettings/EditorBuildSettings.asset` enables `Assets/Scenes/MainScene.unity`. |
| Main scene wires demo-critical AR/backend components. | `Assets/Scenes/MainScene.unity` explicitly attaches `FrameCaptureService`, `HttpPipelineClient`, `ARSurfaceOutlineRenderer`, and `ARSurfaceLockController`, with references from `ButtonController`, `UIManager`, and `ARLabelPlacer`. |
| ARCore packages and Android loader are configured. | `Packages/manifest.json` includes `com.unity.xr.arcore` and `com.unity.xr.arfoundation`; `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` includes `ARCoreLoader` for Android. |
| Android build is constrained toward ARCore-capable devices. | `Assets/XR/Settings/ARCoreSettings.asset` has ARCore Required; `Assets/Plugins/Android/AndroidManifest.xml` declares `android.permission.CAMERA`, `android.hardware.camera.ar` required, and `android:exported="true"` for the launcher activity. |
| Android manifest is syntactically valid. | `Assets/Plugins/Android/AndroidManifest.xml` parses as XML. |
| AR camera raw capture can be confirmed from logs. | `FrameCaptureService` is attached in `MainScene` with the scene `ARCameraManager` assigned. It logs `[FrameCaptureService] Captured frame via ar_camera_raw` when CPU image capture succeeds, logs fallback sources separately, and `ButtonController` surfaces screenshot fallback through the demo status text when debug UI is hidden. |
| Unity Editor can load and compile the project in batchmode. | Unity 2022.3.62f3 batchmode refreshed assets, compiled `Assembly-CSharp`, and exited successfully with return code 0 (`unity-compile.log`). |
| Repeatable Android APK build command exists. | `Assets/Editor/AndroidBuild.cs` provides `AndroidBuild.BuildApk` for Unity batchmode builds to `Builds/Android/ARLectureAssistant.apk`. |
| Project scripts compile outside the Unity Editor. | Roslyn fallback compile over all `Assets/**/*.cs` passed without warnings. |
| Whitespace check is clean. | `git diff --check` passed. |

## Implemented, Needs Device Evidence

| Requirement | Implemented Surface | Missing Evidence |
|---|---|---|
| App runs on Android ARCore device. | Android ARCore loader/settings/manifest are configured. | Physical Android ARCore install and launch result. |
| Five-button UI is available by default. | `MainScene` enables `Hide VN`, transcript control, `Scan`, `Translate`, and `Clear` by default; `ButtonController` / `ARLectureVisualPolish` keep advanced controls hidden through `showAdvancedControls = false` while still preserving the older compact mode flags. | Unity Editor Play Mode or Android screenshot confirming those five controls are visible and non-overlapping. |
| User can lock onto a slide/board. | `ARSurfaceLockController` centralizes search/lock states, disables plane detection after lock, and recovers a locked surface from short `TrackingLimited` interruptions when ARSession returns to tracking. | Android test proving plane lock within 3 seconds in normal light. |
| Surface outline appears in AR. | `ARSurfaceOutlineRenderer` draws locked pose or mapped document corners. | Android visual confirmation that outline aligns with board/slide. |
| Translation labels appear on the physical surface. | `ARDocumentSurfaceMapper` maps OCR bboxes to the AR document surface; `ARLabelPlacer` creates world-space labels and bounded bbox-aware overlap nudges. | Android visual confirmation on real slide/board. |
| OCR bboxes from surface crops remain in original image coordinates. | `DocumentSurfaceService` stores the perspective transform used for the crop and remaps crop OCR points back through that transform; backend tests cover the mapping. | Real OCR run on angled physical slides. |
| Labels remain stable during camera movement. | Labels are created under AR anchors; `ARAnchorPlacer` attaches to hit/mapped `ARPlane` when possible, `ARSurfaceLockController` keeps the locked plane active with visuals hidden after lock, and UI scan/clear/freeze controls route through that controller instead of directly deactivating the locked plane. | Android movement test over 0.5-1 meter left/right and tilt. |
| Friendly retry path works after backend/OCR errors. | `ButtonController` keeps the current locked surface through translation errors and makes `Thử lại` retry translation immediately when tracking is still locked; otherwise it starts a new scan. | Android/backend-offline test confirming retry after restoring backend. |
| Real OCR mode works on at least 4/5 prepared sample slides. | Backend tests cover 4/5 sample slide surface detection and real OCR tests skip cleanly when provider binaries are unavailable. OCR preprocessing includes grayscale, autocontrast, contrast, sharpness, unsharp mask, optional binary thresholding, line merge, and provider fallback. | Real OCR run on the prepared physical/demo slide set with provider configured. |
| The app can be demoed five times consecutively without restart. | Reset path clears labels, outline, debug state, and surface lock through the centralized controller. | Five full Android runs: scan -> translate -> move -> clear. |

## Current Blockers To Final Completion

- A healthy Unity Hub/Editor licensing session is needed for Play Mode/scene visual verification and Android build.
- The batch APK build path is scripted, but the current desktop session stopped before Gradle because Unity Licensing Client IPC timed out with return code 199.
- A physical ARCore-compatible Android device is needed for camera raw, plane lock, anchor stability, and repeatability evidence.
- Real OCR evidence needs the intended OCR provider configured on the demo machine and the prepared slide set captured in real conditions.
