using ARLectureTranslator.AR;
using ARLectureTranslator.Services;
using ARLectureTranslator.UI;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace ARLectureTranslator.EditorTools
{
    public static class ARLectureSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/ARLecture_Task14_Raycast.unity";

        [MenuItem("AR Lecture Assistant/Create Task 1.4 Raycast Scene")]
        public static void CreateTask14Scene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureAndroidXR();

            ARSession arSession = CreateGameObject<ARSession>("AR Session");
            arSession.gameObject.AddComponent<ARInputManager>();

            XROrigin xrOrigin = CreateXrOrigin();
            ARPlaneManager planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            ARRaycastManager raycastManager = xrOrigin.gameObject.AddComponent<ARRaycastManager>();
            ARAnchorManager anchorManager = xrOrigin.gameObject.AddComponent<ARAnchorManager>();

            Canvas canvas = CreateCanvas();
            TMP_Text statusText = CreateText(canvas.transform, "StatusText", "Đang tìm plane tại tâm màn hình...", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(760f, 64f), 24, TextAlignmentOptions.Center);
            TMP_Text debugText = CreateText(canvas.transform, "DebugText", "", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(210f, 130f), new Vector2(380f, 220f), 18, TextAlignmentOptions.TopLeft);
            RectTransform reticle = CreateReticle(canvas.transform);

            Button scanButton = CreateButton(canvas.transform, "ScanButton", "Scan", new Vector2(-170f, 48f));
            Button placeLabelButton = CreateButton(canvas.transform, "PlaceLabelButton", "Place Label", new Vector2(0f, 48f));
            Button clearButton = CreateButton(canvas.transform, "ClearButton", "Clear", new Vector2(170f, 48f));

            GameObject controllerObject = new GameObject("ARLectureTranslatorController");
            FrameCaptureService frameCapture = controllerObject.AddComponent<FrameCaptureService>();
            HttpPipelineClient httpClient = controllerObject.AddComponent<HttpPipelineClient>();
            ARLabelPlacer labelPlacer = controllerObject.AddComponent<ARLabelPlacer>();
            DebugPanelController debugPanel = controllerObject.AddComponent<DebugPanelController>();
            ARLectureTranslatorController controller = controllerObject.AddComponent<ARLectureTranslatorController>();
            ScreenPlaneRaycastTester raycastTester = controllerObject.AddComponent<ScreenPlaneRaycastTester>();

            labelPlacer.raycastManager = raycastManager;
            labelPlacer.anchorManager = anchorManager;
            labelPlacer.arCamera = xrOrigin.Camera;

            debugPanel.debugText = debugText;

            controller.useMockClient = true;
            controller.frameCaptureService = frameCapture;
            controller.httpPipelineClient = httpClient;
            controller.labelPlacer = labelPlacer;
            controller.debugPanel = debugPanel;
            controller.statusText = statusText;
            controller.scanButton = scanButton;
            controller.clearButton = clearButton;

            raycastTester.raycastManager = raycastManager;
            raycastTester.anchorManager = anchorManager;
            raycastTester.planeManager = planeManager;
            raycastTester.arSession = arSession;
            raycastTester.arCameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
            raycastTester.arCamera = xrOrigin.Camera;
            raycastTester.statusText = statusText;
            raycastTester.reticle = reticle;
            raycastTester.testLabelText = "Raycast OK";

            UnityEventTools.AddPersistentListener(scanButton.onClick, controller.OnScanButtonClicked);
            UnityEventTools.AddPersistentListener(placeLabelButton.onClick, raycastTester.OnPlaceLabelButtonClicked);
            UnityEventTools.AddPersistentListener(clearButton.onClick, controller.OnClearButtonClicked);
            UnityEventTools.AddPersistentListener(clearButton.onClick, raycastTester.ClearPlacedLabels);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            Selection.activeObject = controllerObject;
            Debug.Log($"Created Task 1.4 raycast scene at {ScenePath}");
        }

        public static void ConfigureAndroidXR()
        {
            XRGeneralSettingsPerBuildTarget buildTargetSettings = FindOrCreateXRGeneralSettingsPerBuildTarget();
            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            XRManagerSettings managerSettings = buildTargetSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            managerSettings.automaticLoading = true;
            managerSettings.automaticRunning = true;
            const string arCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
            bool assigned = XRPackageMetadataStore.AssignLoader(managerSettings, arCoreLoaderType, BuildTargetGroup.Android);
            if (!assigned)
            {
                Debug.LogWarning("ARCore loader was already assigned or could not be assigned automatically.");
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargetSettings, true);
            EditorUtility.SetDirty(buildTargetSettings);
            EditorUtility.SetDirty(managerSettings);
            AssetDatabase.SaveAssets();
        }

        private static XRGeneralSettingsPerBuildTarget FindOrCreateXRGeneralSettingsPerBuildTarget()
        {
            string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (guids.Length > 0)
            {
                string existingPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(existingPath);
            }

            const string xrFolder = "Assets/XR";
            if (!AssetDatabase.IsValidFolder(xrFolder))
            {
                AssetDatabase.CreateFolder("Assets", "XR");
            }

            var settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(settings, $"{xrFolder}/XRGeneralSettingsPerBuildTarget.asset");
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static T CreateGameObject<T>(string name) where T : Component
        {
            GameObject gameObject = new GameObject(name);
            return gameObject.AddComponent<T>();
        }

        private static XROrigin CreateXrOrigin()
        {
            GameObject originObject = new GameObject("XR Origin (AR)");
            XROrigin xrOrigin = originObject.AddComponent<XROrigin>();

            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originObject.transform, false);

            GameObject cameraObject = new GameObject("AR Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();

            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            xrOrigin.Camera = camera;
            return xrOrigin;
        }

        private static Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();

            return canvas;
        }

        private static TMP_Text CreateText(Transform parent, string name, string value, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition)
        {
            GameObject buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(150f, 58f);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.06f, 0.08f, 0.1f, 0.84f);
            Button button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.12f, 0.16f, 0.2f, 0.92f);
            colors.pressedColor = new Color(0.03f, 0.05f, 0.07f, 0.96f);
            button.colors = colors;

            CreateText(buttonObject.transform, "Label", label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 22, TextAlignmentOptions.Center);
            return button;
        }

        private static RectTransform CreateReticle(Transform parent)
        {
            TMP_Text reticleText = CreateText(parent, "Reticle", "+", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f), 44, TextAlignmentOptions.Center);
            reticleText.color = new Color(1f, 1f, 1f, 0.9f);
            return reticleText.GetComponent<RectTransform>();
        }
    }
}
