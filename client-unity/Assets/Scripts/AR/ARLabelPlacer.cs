using System.Collections.Generic;
using System.Threading.Tasks;
using ARLectureTranslator.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARLectureTranslator.AR
{
    /// <summary>
    /// Nhận bbox 2D từ backend, raycast tâm bbox vào AR plane, tạo anchor và label ảo.
    /// MVP hiện đặt label tại tâm bbox. Để "đè đúng lên đoạn văn" hơn, cần homography/plane mapping.
    /// </summary>
    public class ARLabelPlacer : MonoBehaviour
    {
        [Header("AR References")]
        public ARRaycastManager raycastManager;
        public ARAnchorManager anchorManager;
        public Camera arCamera;

        [Header("Label Style")]
        [Tooltip("Scale cơ bản của world-space canvas. Tăng nếu label quá nhỏ.")]
        public float baseLabelScale = 0.0015f;

        [Tooltip("Đẩy label ra khỏi mặt phẳng một chút để tránh z-fighting.")]
        public float surfaceOffsetMeters = 0.015f;

        [Tooltip("Nếu true, bỏ qua block confidence thấp.")]
        public bool filterLowConfidence = true;

        [Range(0f, 1f)]
        public float minConfidence = 0.45f;

        private static readonly List<ARRaycastHit> Hits = new();
        private readonly List<GameObject> spawnedLabels = new();

        public async Task<int> PlaceLabelsAsync(PipelineResponse response)
        {
            if (response == null || response.blocks == null) return 0;

            int placed = 0;
            foreach (PipelineBlock block in response.blocks)
            {
                if (block == null || block.bbox == null || block.bbox.Length < 4) continue;
                if (filterLowConfidence && block.confidence < minConfidence) continue;

                bool ok = await PlaceSingleLabelAsync(block, response.image_width, response.image_height);
                if (ok) placed++;
            }

            return placed;
        }

        public async Task<bool> PlaceSingleLabelAsync(PipelineBlock block, int imageWidth, int imageHeight)
        {
            if (raycastManager == null || anchorManager == null || arCamera == null)
            {
                Debug.LogError("ARLabelPlacer thiếu raycastManager / anchorManager / arCamera.");
                return false;
            }

            Vector2 screenPoint = BBoxCenterToScreenPoint(block.bbox, imageWidth, imageHeight);

            bool hasHit = raycastManager.Raycast(screenPoint, Hits, TrackableType.PlaneWithinPolygon);
            if (!hasHit)
            {
                Debug.LogWarning($"Không raycast được bbox của block {block.id}. Hãy quét plane rõ hơn.");
                return false;
            }

            Pose pose = Hits[0].pose;
            Vector3 offsetPosition = pose.position + pose.rotation * Vector3.forward * surfaceOffsetMeters;
            Pose offsetPose = new Pose(offsetPosition, pose.rotation);

            // AR Foundation 6.x: TryAddAnchorAsync.
            // TODO(MVP): Nếu nhóm dùng AR Foundation 5.x, kiểm tra lại API AddAnchor/TryAddAnchor theo version.
            var result = await anchorManager.TryAddAnchorAsync(offsetPose);
            if (!result.status.IsSuccess())
            {
                Debug.LogWarning($"Tạo anchor thất bại cho block {block.id}: {result.status}");
                return false;
            }

            ARAnchor anchor = result.value;
            GameObject label = CreateWorldSpaceLabel(block);
            label.transform.SetParent(anchor.transform, worldPositionStays: false);
            label.transform.localPosition = Vector3.zero;
            label.transform.localRotation = Quaternion.identity;

            var billboard = label.AddComponent<BillboardToCamera>();
            billboard.targetCamera = arCamera.transform;

            spawnedLabels.Add(anchor.gameObject);
            return true;
        }

        public void ClearLabels()
        {
            foreach (GameObject root in spawnedLabels)
            {
                if (root != null) Destroy(root);
            }
            spawnedLabels.Clear();
        }

        private Vector2 BBoxCenterToScreenPoint(float[] bbox, int imageWidth, int imageHeight)
        {
            float x1 = bbox[0];
            float y1 = bbox[1];
            float x2 = bbox[2];
            float y2 = bbox[3];

            float cx = (x1 + x2) * 0.5f;
            float cy = (y1 + y2) * 0.5f;

            float sx = imageWidth > 0 ? cx / imageWidth * Screen.width : Screen.width * 0.5f;

            // Backend dùng origin top-left; Unity screen point dùng origin bottom-left.
            float sy = imageHeight > 0 ? Screen.height - (cy / imageHeight * Screen.height) : Screen.height * 0.5f;

            return new Vector2(sx, sy);
        }

        private GameObject CreateWorldSpaceLabel(PipelineBlock block)
        {
            string labelText = string.IsNullOrWhiteSpace(block.translated_text)
                ? block.source_text
                : block.translated_text;

            GameObject root = new GameObject($"AR_Label_{block.id}");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();

            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(720, 180);
            root.transform.localScale = Vector3.one * baseLabelScale;

            GameObject panel = new GameObject("Background");
            panel.transform.SetParent(root.transform, worldPositionStays: false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var image = panel.AddComponent<Image>();
            float alpha = block.style != null ? block.style.background_alpha : 0.65f;
            image.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, worldPositionStays: false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24, 16);
            textRect.offsetMax = new Vector2(-24, -16);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = labelText;
            tmp.color = Color.white;
            tmp.fontSize = block.style != null ? block.style.font_size : 36;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Công thức nên giữ monospace/ít wrap hơn nếu sau này có font phù hợp.
            // TODO(MVP): Gắn font fallback có ký hiệu toán học tốt hơn: Noto Sans Math, Latin Modern Math.
            if (block.type == "formula")
            {
                tmp.fontSize = Mathf.Max(28, tmp.fontSize - 4);
                tmp.alignment = TextAlignmentOptions.Midline;
            }

            return root;
        }
    }
}
