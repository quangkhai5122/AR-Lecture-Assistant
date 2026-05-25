// ARLabelPlacer.cs — Hỗ trợ multi-label
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR.ARFoundation;

public class ARLabelPlacer : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject fixedLabelPrefab;    // World Space label
    [SerializeField] private GameObject subtitlePrefab;       // Screen Space subtitle

    [Header("References")]
    [SerializeField] private ARAnchorPlacer anchorPlacer;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private Transform subtitleContainer;     // Parent cho subtitle

    // Quản lý nhiều label cùng lúc
    private List<GameObject> fixedLabels = new List<GameObject>();
    private GameObject currentSubtitle;

    /// <summary>
    /// Đặt Fixed Label bám trên slide/bảng tại vị trí tap
    /// Có thể đặt nhiều label cùng lúc
    /// </summary>
    public void PlaceFixedLabel(string translatedText, Vector2 screenPos)
    {
        TryPlaceFixedLabel(translatedText, screenPos);
    }

    public bool TryPlaceFixedLabel(string translatedText, Vector2 screenPos)
    {
        if (raycastController.TryRaycast(screenPos, out Pose hitPose))
        {
            // Tạo anchor để text bám ổn định
            ARAnchor anchor = anchorPlacer.PlaceAnchor(hitPose);

            // Instantiate label dưới anchor
            GameObject label = Instantiate(fixedLabelPrefab, anchor.transform);
            label.transform.localPosition = Vector3.zero;

            // Set text
            var textComp = label.GetComponentInChildren<TextMeshProUGUI>();
            if (textComp != null)
                textComp.text = translatedText;

            ARLectureVisualPolish.StyleLabel(label);
            fixedLabels.Add(label);
            return true;
        }

        return false;
    }

    public int PlacePipelineLabels(PipelineResponse response)
    {
        if (response == null || response.blocks == null) return 0;

        int placed = 0;
        foreach (PipelineBlock block in response.blocks)
        {
            if (block == null || block.bbox == null || block.bbox.Length < 4) continue;

            string text = string.IsNullOrWhiteSpace(block.translated_text)
                ? block.source_text
                : block.translated_text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            Vector2 screenPoint = BBoxCenterToScreenPoint(block.bbox, response.image_width, response.image_height);
            if (TryPlaceFixedLabel(text, screenPoint))
            {
                placed++;
            }
        }

        return placed;
    }

    private Vector2 BBoxCenterToScreenPoint(float[] bbox, int imageWidth, int imageHeight)
    {
        float centerX = (bbox[0] + bbox[2]) * 0.5f;
        float centerY = (bbox[1] + bbox[3]) * 0.5f;

        float screenX = imageWidth > 0
            ? centerX / imageWidth * Screen.width
            : Screen.width * 0.5f;

        // Backend trả bbox theo gốc top-left; Unity screen point dùng gốc bottom-left.
        float screenY = imageHeight > 0
            ? Screen.height - (centerY / imageHeight * Screen.height)
            : Screen.height * 0.5f;

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Hiển thị/cập nhật Subtitle ở dưới màn hình
    /// Chỉ có 1 subtitle tại 1 thời điểm
    /// </summary>
    public void ShowSubtitle(string translatedText)
    {
        if (currentSubtitle == null)
        {
            currentSubtitle = Instantiate(subtitlePrefab, subtitleContainer);
        }

        var textComp = currentSubtitle.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null)
            textComp.text = translatedText;

        ARLectureVisualPolish.StyleSubtitle(currentSubtitle);
    }

    /// <summary>
    /// Ẩn subtitle
    /// </summary>
    public void HideSubtitle()
    {
        if (currentSubtitle != null)
        {
            Destroy(currentSubtitle);
            currentSubtitle = null;
        }
    }

    /// <summary>
    /// Xóa tất cả Fixed Labels (giữ subtitle)
    /// </summary>
    public void ClearFixedLabels()
    {
        foreach (var label in fixedLabels)
        {
            if (label != null)
            {
                // Xóa cả anchor parent
                var anchor = label.GetComponentInParent<ARAnchor>();
                if (anchor != null)
                    Destroy(anchor.gameObject);
                else
                    Destroy(label);
            }
        }
        fixedLabels.Clear();
    }

    /// <summary>
    /// Xóa tất cả (cả Fixed + Subtitle)
    /// </summary>
    public void ClearAll()
    {
        ClearFixedLabels();
        HideSubtitle();
    }
}
