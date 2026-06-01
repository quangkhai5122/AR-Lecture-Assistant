using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLectureTranslator.AR
{
    /// <summary>
    /// Nhiệm vụ 1.4: raycast từ điểm trên màn hình vào AR plane và đặt label thử.
    /// Dùng script này để kiểm tra nhanh plane detection + screen-to-world mapping trước khi nối OCR.
    /// </summary>
    public class ScreenPlaneRaycastTester : MonoBehaviour
    {
        [Header("AR References")]
        public ARRaycastManager raycastManager;
        public ARAnchorManager anchorManager;
        public ARPlaneManager planeManager;
        public ARSession arSession;
        public ARCameraManager arCameraManager;
        public Camera arCamera;

        [Header("UI")]
        public TMP_Text statusText;
        public RectTransform reticle;

        [Header("Placement")]
        public bool enableTapToPlace = true;
        public string testLabelText = "Place Label";
        public float labelScale = 0.0015f;
        public float surfaceOffsetMeters = 0.015f;
        public bool showFallbackDebugOverlay = true;
        public float autoRestartReadyDelaySeconds = 4f;
        public bool enableNonArCameraFallback = true;
        public float nonArFallbackDelaySeconds = 7f;

        private static readonly List<ARRaycastHit> Hits = new();
        private readonly List<GameObject> spawnedAnchors = new();
        private readonly List<Vector2> fallbackLabelPoints = new();
        private bool isPlacing;
        private bool centerHasHit;
        private string lastStatusMessage = "Đang khởi động AR...";
        private GUIStyle debugStyle;
        private GUIStyle fallbackLabelStyle;
        private GUIStyle buttonStyle;
        private float readySince = -1f;
        private bool hasAutoRestarted;
        private bool nonArFallbackActive;
        private WebCamTexture fallbackCameraTexture;
        private RawImage fallbackCameraPreview;
        private RectTransform fallbackCameraPreviewRect;

        private void Update()
        {
            WatchArSessionStartup();
            UpdateFallbackCameraPreview();

            centerHasHit = !nonArFallbackActive && TryGetPlanePose(GetScreenCenter(), out Pose centerPose);
            UpdateReticle(centerHasHit);
            UpdateStatus(centerHasHit);

            if (!enableTapToPlace || isPlacing) return;

            if (TryGetTapPosition(out Vector2 tapPosition))
            {
                _ = PlaceAtScreenPointAsync(tapPosition);
            }
        }

        public async void OnPlaceLabelButtonClicked()
        {
            await PlaceAtScreenPointAsync(GetScreenCenter());
        }

        public void ClearPlacedLabels()
        {
            foreach (GameObject anchor in spawnedAnchors)
            {
                if (anchor != null) Destroy(anchor);
            }

            spawnedAnchors.Clear();
            fallbackLabelPoints.Clear();
            SetStatus("Đã xóa label test.");
        }

        public void RestartARSession()
        {
            StopNonArCameraFallback();

            if (arSession == null)
            {
                SetStatus("Không tìm thấy ARSession để restart.");
                return;
            }

            arSession.Reset();
            arSession.enabled = false;
            arSession.enabled = true;
            readySince = Time.time;
            hasAutoRestarted = true;
            SetStatus("Đã restart AR session. Hãy lia camera chậm quanh mặt phẳng.");
        }

        public async Task<bool> PlaceAtScreenPointAsync(Vector2 screenPoint)
        {
            if (isPlacing) return false;
            isPlacing = true;

            try
            {
                if (nonArFallbackActive)
                {
                    fallbackLabelPoints.Add(screenPoint);
                    SetStatus("Fallback camera: đã đặt label demo tại điểm màn hình. Đây không phải ARCore anchor thật.");
                    return true;
                }

                if (!TryGetPlanePose(screenPoint, out Pose planePose))
                {
                    SetStatus("Chưa raycast được vào plane. Hãy lia camera chậm qua bảng/slide.");
                    return false;
                }

                Pose labelPose = OffsetFromPlane(planePose);
#if UNITY_6000_0_OR_NEWER
                var result = await anchorManager.TryAddAnchorAsync(labelPose);
                if (!result.status.IsSuccess())
                {
                    SetStatus($"Tạo anchor thất bại: {result.status}");
                    return false;
                }

                ARAnchor anchor = result.value;
#else
                await Task.Yield();
                ARAnchor anchor = anchorManager.AddAnchor(labelPose);
                if (anchor == null)
                {
                    SetStatus("Tạo anchor thất bại.");
                    return false;
                }
#endif
                GameObject label = CreateTestLabel();
                label.transform.SetParent(anchor.transform, worldPositionStays: false);
                label.transform.localPosition = Vector3.zero;
                label.transform.localRotation = Quaternion.identity;

                var billboard = label.AddComponent<BillboardToCamera>();
                billboard.targetCamera = arCamera != null ? arCamera.transform : null;

                spawnedAnchors.Add(anchor.gameObject);
                SetStatus("Raycast thành công: đã đặt label test lên plane.");
                return true;
            }
            finally
            {
                isPlacing = false;
            }
        }

        private bool TryGetPlanePose(Vector2 screenPoint, out Pose pose)
        {
            pose = default;

            if (raycastManager == null || anchorManager == null)
            {
                SetStatus("Thiếu ARRaycastManager hoặc ARAnchorManager.");
                return false;
            }

            bool hasHit = raycastManager.Raycast(screenPoint, Hits, TrackableType.PlaneWithinPolygon);
            if (!hasHit) return false;

            pose = Hits[0].pose;
            return true;
        }

        private Pose OffsetFromPlane(Pose planePose)
        {
            Vector3 offsetPosition = planePose.position + planePose.rotation * Vector3.forward * surfaceOffsetMeters;
            return new Pose(offsetPosition, planePose.rotation);
        }

        private Vector2 GetScreenCenter()
        {
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        private bool TryGetTapPosition(out Vector2 screenPoint)
        {
            screenPoint = default;

            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase != TouchPhase.Began || IsPointerOverUi(touch.fingerId)) return false;

                screenPoint = touch.position;
                return true;
            }

#if UNITY_EDITOR
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi(-1))
            {
                screenPoint = Input.mousePosition;
                return true;
            }
#endif

            return false;
        }

        private bool IsPointerOverUi(int pointerId)
        {
            if (EventSystem.current == null) return false;
            return pointerId >= 0
                ? EventSystem.current.IsPointerOverGameObject(pointerId)
                : EventSystem.current.IsPointerOverGameObject();
        }

        private void UpdateReticle(bool hasHit)
        {
            if (reticle == null) return;

            reticle.gameObject.SetActive(true);
            reticle.anchoredPosition = Vector2.zero;
            reticle.localScale = hasHit ? Vector3.one : Vector3.one * 0.8f;
        }

        private void UpdateStatus(bool hasHit)
        {
            if (isPlacing) return;

            if (nonArFallbackActive)
            {
                SetStatus("Fallback camera đang chạy. Bấm Place để đặt label demo trên màn hình.");
                return;
            }

            SetStatus(hasHit
                ? "Plane sẵn sàng. Bấm Place Label hoặc chạm màn hình để test raycast."
                : "Đang tìm plane tại tâm màn hình...");
        }

        private void SetStatus(string message)
        {
            lastStatusMessage = message;
            if (statusText != null) statusText.text = message;
        }

        private void WatchArSessionStartup()
        {
            if (ARSession.state == ARSessionState.Ready)
            {
                if (readySince < 0f) readySince = Time.time;

                if (!hasAutoRestarted && Time.time - readySince > autoRestartReadyDelaySeconds)
                {
                    RestartARSession();
                }
                else if (enableNonArCameraFallback &&
                         hasAutoRestarted &&
                         Time.time - readySince > nonArFallbackDelaySeconds)
                {
                    StartNonArCameraFallback("ARCore vẫn kẹt Ready, chuyển sang camera thường để demo.");
                }

                return;
            }

            if (ARSession.state == ARSessionState.SessionInitializing ||
                ARSession.state == ARSessionState.SessionTracking)
            {
                hasAutoRestarted = true;
            }

            readySince = -1f;
        }

        private void StartNonArCameraFallback(string reason)
        {
            if (nonArFallbackActive) return;

            nonArFallbackActive = true;
            if (arSession != null) arSession.enabled = false;
            if (arCameraManager != null) arCameraManager.enabled = false;
            if (planeManager != null) planeManager.enabled = false;
            if (raycastManager != null) raycastManager.enabled = false;
            if (anchorManager != null) anchorManager.enabled = false;

            EnsureFallbackCameraPreview();

            WebCamDevice? backCamera = null;
            foreach (WebCamDevice device in WebCamTexture.devices)
            {
                if (!device.isFrontFacing)
                {
                    backCamera = device;
                    break;
                }
            }

            fallbackCameraTexture = backCamera.HasValue
                ? new WebCamTexture(backCamera.Value.name, Screen.width, Screen.height)
                : new WebCamTexture(Screen.width, Screen.height);
            fallbackCameraPreview.texture = fallbackCameraTexture;
            fallbackCameraTexture.Play();
            SetStatus($"{reason} Camera thường đã bật; phần này dùng để test UI/UX và luồng scan/place.");
        }

        private void StopNonArCameraFallback()
        {
            nonArFallbackActive = false;
            fallbackLabelPoints.Clear();

            if (fallbackCameraTexture != null)
            {
                if (fallbackCameraTexture.isPlaying) fallbackCameraTexture.Stop();
                fallbackCameraTexture = null;
            }

            if (fallbackCameraPreview != null)
            {
                Destroy(fallbackCameraPreview.gameObject.transform.parent.gameObject);
                fallbackCameraPreview = null;
                fallbackCameraPreviewRect = null;
            }

            if (arSession != null) arSession.enabled = true;
            if (arCameraManager != null) arCameraManager.enabled = true;
            if (planeManager != null) planeManager.enabled = true;
            if (raycastManager != null) raycastManager.enabled = true;
            if (anchorManager != null) anchorManager.enabled = true;
        }

        private void EnsureFallbackCameraPreview()
        {
            if (fallbackCameraPreview != null) return;

            GameObject canvasObject = new GameObject("FallbackCameraCanvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;

            GameObject previewObject = new GameObject("FallbackCameraPreview");
            previewObject.transform.SetParent(canvasObject.transform, false);
            fallbackCameraPreview = previewObject.AddComponent<RawImage>();
            fallbackCameraPreview.color = Color.white;

            fallbackCameraPreviewRect = fallbackCameraPreview.rectTransform;
            fallbackCameraPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
            fallbackCameraPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
            fallbackCameraPreviewRect.anchoredPosition = Vector2.zero;
        }

        private void UpdateFallbackCameraPreview()
        {
            if (!nonArFallbackActive ||
                fallbackCameraTexture == null ||
                fallbackCameraPreview == null ||
                fallbackCameraPreviewRect == null) return;

            if (fallbackCameraTexture.width <= 16 || fallbackCameraTexture.height <= 16) return;

            fallbackCameraPreview.uvRect = fallbackCameraTexture.videoVerticallyMirrored
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);

            int rotation = fallbackCameraTexture.videoRotationAngle;
            bool portraitScreen = Screen.height >= Screen.width;
            bool landscapeFrame = fallbackCameraTexture.width >= fallbackCameraTexture.height;
            if (rotation == 0 && portraitScreen && landscapeFrame)
            {
                rotation = 90;
            }

            bool swapDimensions = rotation == 90 || rotation == 270;
            float videoWidth = swapDimensions ? fallbackCameraTexture.height : fallbackCameraTexture.width;
            float videoHeight = swapDimensions ? fallbackCameraTexture.width : fallbackCameraTexture.height;
            float videoAspect = videoWidth / videoHeight;
            float screenAspect = (float)Screen.width / Screen.height;

            float targetWidth;
            float targetHeight;
            if (videoAspect > screenAspect)
            {
                targetHeight = Screen.height;
                targetWidth = targetHeight * videoAspect;
            }
            else
            {
                targetWidth = Screen.width;
                targetHeight = targetWidth / videoAspect;
            }

            fallbackCameraPreviewRect.sizeDelta = new Vector2(targetWidth, targetHeight);
            fallbackCameraPreviewRect.localEulerAngles = new Vector3(0f, 0f, -rotation);
        }

        private void OnGUI()
        {
            if (!showFallbackDebugOverlay) return;

            debugStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(28, Screen.height / 42),
                normal = { textColor = Color.white },
                wordWrap = true
            };

            buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.Max(26, Screen.height / 44)
            };

            fallbackLabelStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.Max(24, Screen.height / 48),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                wordWrap = true
            };

            float margin = 24f;
            float panelWidth = Screen.width - margin * 2f;
            float panelHeight = Mathf.Min(Screen.height * 0.34f, 360f);
            Rect panelRect = new Rect(margin, margin, panelWidth, panelHeight);

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(panelRect, GUIContent.none);
            GUI.color = Color.white;

            string cameraState = arCameraManager != null && arCameraManager.enabled ? "enabled" : "missing/disabled";
            int planeCount = planeManager != null ? planeManager.trackables.count : -1;
            string planeText = planeCount >= 0 ? planeCount.ToString() : "n/a";
            string modeText = nonArFallbackActive ? "Camera fallback" : "ARCore";
            string supportHint =
#if UNITY_EDITOR
                "Editor mode: camera thật thường đen. Build APK lên điện thoại Android có ARCore để test camera.";
#else
                nonArFallbackActive
                    ? "Fallback dùng camera thường: đủ để demo UI/scan/place, không tạo ARCore anchor thật."
                    : "Nếu vẫn đen: kiểm tra quyền Camera và Google Play Services for AR.";
#endif

            string stateHint = nonArFallbackActive
                ? "Bấm Place để đặt label demo. Bấm Restart để thử ARCore lại."
                : ARSession.state == ARSessionState.Ready
                ? "AR Ready nhưng chưa tracking. Bấm Restart hoặc mở lại app nếu kẹt lâu."
                : "Khi Planes > 0 và Center hit=True thì bấm Place.";

            string text =
                $"Mode: {modeText} | AR state: {ARSession.state}\n" +
                $"Camera: {cameraState} | Planes: {planeText} | Center hit: {centerHasHit}\n" +
                $"{lastStatusMessage}\n" +
                $"{stateHint}\n" +
                supportHint;

            GUI.Label(new Rect(margin + 18f, margin + 16f, panelWidth - 36f, panelHeight - 32f), text, debugStyle);

            float buttonHeight = Mathf.Max(72f, Screen.height * 0.075f);
            float buttonWidth = (Screen.width - margin * 5f) / 4f;
            float y = Screen.height - buttonHeight - margin;

            if (GUI.Button(new Rect(margin, y, buttonWidth, buttonHeight), "Place", buttonStyle))
            {
                _ = PlaceAtScreenPointAsync(GetScreenCenter());
            }

            if (GUI.Button(new Rect(margin * 2f + buttonWidth, y, buttonWidth, buttonHeight), "Clear", buttonStyle))
            {
                ClearPlacedLabels();
            }

            if (GUI.Button(new Rect(margin * 3f + buttonWidth * 2f, y, buttonWidth, buttonHeight), "Restart", buttonStyle))
            {
                RestartARSession();
            }

            if (GUI.Button(new Rect(margin * 4f + buttonWidth * 3f, y, buttonWidth, buttonHeight), "Help", buttonStyle))
            {
                if (nonArFallbackActive)
                {
                    SetStatus("Fallback đang dùng camera thường như bản demo. Restart để thử lại ARCore raycast thật.");
                }
                else
                {
                    SetStatus("Test trên điện thoại có ARCore. Lia camera theo vòng tròn quanh mặt bàn/tường sáng, có chi tiết.");
                }
            }

            foreach (Vector2 point in fallbackLabelPoints)
            {
                Rect labelRect = new Rect(point.x - 170f, Screen.height - point.y - 42f, 340f, 84f);
                GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.78f);
                GUI.Box(labelRect, testLabelText, fallbackLabelStyle);
                GUI.color = Color.white;
            }
        }

        private GameObject CreateTestLabel()
        {
            GameObject root = new GameObject("Raycast_Test_Label");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();

            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(520, 140);
            root.transform.localScale = Vector3.one * labelScale;

            GameObject panel = new GameObject("Background");
            panel.transform.SetParent(root.transform, worldPositionStays: false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var image = panel.AddComponent<Image>();
            image.color = new Color(0.02f, 0.02f, 0.02f, 0.72f);

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, worldPositionStays: false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20, 14);
            textRect.offsetMax = new Vector2(-20, -14);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = testLabelText;
            text.color = Color.white;
            text.fontSize = 34;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;

            return root;
        }
    }
}
