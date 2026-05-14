using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ARLectureTranslator.Services
{
    public struct CapturedFrame
    {
        public string frameId;
        public string imageBase64;
        public int width;
        public int height;
    }

    /// <summary>
    /// MVP: capture screenshot thành JPG base64.
    /// Cách này dễ chạy trong Unity nhưng không tối ưu cho AR camera real-time.
    /// TODO(MVP): Thay bằng ARCameraManager.TryAcquireLatestCpuImage để lấy camera image raw.
    /// TODO(MVP): Resize ảnh trước khi gửi backend để giảm latency.
    /// </summary>
    public class FrameCaptureService : MonoBehaviour
    {
        [Range(10, 95)]
        public int jpegQuality = 65;

        public async Task<CapturedFrame> CaptureAsync()
        {
            // Đợi cuối frame để screenshot có nội dung mới nhất.
            await Task.Yield();

            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
            {
                throw new InvalidOperationException("Cannot capture screen texture.");
            }

            byte[] jpg = texture.EncodeToJPG(jpegQuality);
            string base64 = Convert.ToBase64String(jpg);

            var frame = new CapturedFrame
            {
                frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                imageBase64 = base64,
                width = texture.width,
                height = texture.height
            };

            Destroy(texture);
            return frame;
        }
    }
}
