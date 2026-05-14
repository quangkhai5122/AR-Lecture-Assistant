using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARLectureTranslator.AR
{
    /// <summary>
    /// Hiển thị trạng thái số lượng plane đã phát hiện.
    /// Optional, dùng để debug khi demo.
    /// </summary>
    public class PlaneTrackingStatus : MonoBehaviour
    {
        public ARPlaneManager planeManager;
        public TMP_Text statusText;

        private void Update()
        {
            if (planeManager == null || statusText == null) return;

            int count = planeManager.trackables.count;
            if (count <= 0)
            {
                statusText.text = "Đang tìm mặt phẳng bảng/slide...";
            }
            else
            {
                statusText.text = $"Đã tìm thấy {count} mặt phẳng. Có thể Scan.";
            }
        }
    }
}
