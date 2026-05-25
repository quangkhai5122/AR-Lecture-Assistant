using System;
using System.Threading.Tasks;
using UnityEngine;

public struct CapturedFrame
{
    public string frameId;
    public string imageBase64;
    public int width;
    public int height;
}

/// <summary>
/// MVP capture: chụp frame màn hình thành JPG base64 để gửi backend OCR/Translate.
/// Sau MVP có thể thay bằng ARCameraManager.TryAcquireLatestCpuImage để lấy ảnh camera raw.
/// </summary>
public class FrameCaptureService : MonoBehaviour
{
    [Range(10, 95)]
    public int jpegQuality = 65;

    [Tooltip("Giới hạn cạnh dài nhất của ảnh gửi backend. Đặt 0 để tắt resize.")]
    [Range(0, 4096)]
    public int maxImageDimension = 1280;

    public async Task<CapturedFrame> CaptureAsync()
    {
        await Task.Yield();

        Texture2D capturedTexture = ScreenCapture.CaptureScreenshotAsTexture();
        if (capturedTexture == null)
        {
            throw new InvalidOperationException("Cannot capture screen texture.");
        }

        Texture2D uploadTexture = ResizeIfNeeded(capturedTexture);
        try
        {
            byte[] jpg = uploadTexture.EncodeToJPG(jpegQuality);
            return new CapturedFrame
            {
                frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                imageBase64 = Convert.ToBase64String(jpg),
                width = uploadTexture.width,
                height = uploadTexture.height
            };
        }
        finally
        {
            if (uploadTexture != capturedTexture)
            {
                Destroy(uploadTexture);
            }

            Destroy(capturedTexture);
        }
    }

    private Texture2D ResizeIfNeeded(Texture2D source)
    {
        if (maxImageDimension <= 0) return source;

        int longestSide = Mathf.Max(source.width, source.height);
        if (longestSide <= maxImageDimension) return source;

        float scale = maxImageDimension / (float)longestSide;
        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

        RenderTexture previous = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;

            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();
            return resized;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }
}
