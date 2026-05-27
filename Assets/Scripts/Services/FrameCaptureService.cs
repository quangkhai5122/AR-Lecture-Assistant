using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public struct CapturedFrame
{
    public string frameId;
    public string imageBase64;
    public int width;
    public int height;
}

public enum FrameCaptureSource
{
    Auto,
    ARCameraRaw,
    Screenshot
}

/// <summary>
/// Chụp frame camera thành JPG base64 để gửi backend OCR/Translate.
/// Auto ưu tiên ARCameraManager.TryAcquireLatestCpuImage để tránh dính UI overlay,
/// rồi fallback về screenshot nếu thiết bị/Editor chưa cấp CPU image.
/// </summary>
public class FrameCaptureService : MonoBehaviour
{
    [Header("Capture Source")]
    public FrameCaptureSource captureSource = FrameCaptureSource.Auto;
    [SerializeField] private ARCameraManager arCameraManager;

    [Range(10, 95)]
    public int jpegQuality = 65;

    [Tooltip("Giới hạn cạnh dài nhất của ảnh gửi backend. Đặt 0 để tắt resize.")]
    [Range(0, 4096)]
    public int maxImageDimension = 1280;

    [Tooltip("Một số thiết bị cần lật ảnh CPU camera theo trục Y để khớp texture Unity.")]
    public bool mirrorCameraImageY = true;

    public Task<CapturedFrame> CaptureAsync()
    {
        var tcs = new TaskCompletionSource<CapturedFrame>();
        StartCoroutine(CaptureCoroutine(tcs));
        return tcs.Task;
    }

    private IEnumerator CaptureCoroutine(TaskCompletionSource<CapturedFrame> tcs)
    {
        // Bước 1: Thử AR Camera Raw (không cần WaitForEndOfFrame)
        if (captureSource != FrameCaptureSource.Screenshot &&
            TryCaptureARCameraRaw(out CapturedFrame cameraFrame))
        {
            tcs.TrySetResult(cameraFrame);
            yield break;
        }

        if (captureSource == FrameCaptureSource.ARCameraRaw)
        {
            tcs.TrySetException(new InvalidOperationException("Cannot acquire AR camera CPU image."));
            yield break;
        }

        // Bước 2: Screenshot — BẮT BUỘC phải đợi WaitForEndOfFrame trên Android
        yield return new WaitForEndOfFrame();

        try
        {
            CapturedFrame frame = CaptureScreenshotFrame();
            tcs.TrySetResult(frame);
        }
        catch (Exception)
        {
            // Bước 3: Fallback — đọc pixels từ Camera.main
            try
            {
                CapturedFrame frame = CaptureCameraReadPixels();
                tcs.TrySetResult(frame);
            }
            catch (Exception ex2)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"All capture methods failed: {ex2.Message}"));
            }
        }
    }

    private bool TryCaptureARCameraRaw(out CapturedFrame frame)
    {
        frame = default;

        if (arCameraManager == null)
        {
            arCameraManager = FindAnyObjectByType<ARCameraManager>();
        }

        if (arCameraManager == null ||
            !arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            return false;
        }

        using (cpuImage)
        {
            Vector2Int outputDimensions = ResolveOutputDimensions(cpuImage.width, cpuImage.height);
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = outputDimensions,
                outputFormat = TextureFormat.RGBA32,
                transformation = mirrorCameraImageY
                    ? XRCpuImage.Transformation.MirrorY
                    : XRCpuImage.Transformation.None
            };

            Texture2D cameraTexture = new Texture2D(
                outputDimensions.x,
                outputDimensions.y,
                conversionParams.outputFormat,
                false
            );

            try
            {
                NativeArray<byte> rawTextureData = cameraTexture.GetRawTextureData<byte>();
                cpuImage.Convert(conversionParams, rawTextureData);
                cameraTexture.Apply();

                byte[] jpg = cameraTexture.EncodeToJPG(jpegQuality);
                frame = new CapturedFrame
                {
                    frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                    imageBase64 = Convert.ToBase64String(jpg),
                    width = cameraTexture.width,
                    height = cameraTexture.height
                };
                return true;
            }
            finally
            {
                Destroy(cameraTexture);
            }
        }
    }

    private CapturedFrame CaptureScreenshotFrame()
    {
        Texture2D capturedTexture = ScreenCapture.CaptureScreenshotAsTexture();
        if (capturedTexture == null)
        {
            throw new InvalidOperationException("Screenshot returned null.");
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

    /// <summary>
    /// Fallback: đọc pixels trực tiếp từ Camera.main RenderTexture
    /// </summary>
    private CapturedFrame CaptureCameraReadPixels()
    {
        Camera cam = Camera.main;
        if (cam == null)
            throw new InvalidOperationException("Camera.main is null.");

        int w = Screen.width;
        int h = Screen.height;
        Vector2Int size = ResolveOutputDimensions(w, h);

        RenderTexture rt = new RenderTexture(size.x, size.y, 24);
        RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        Destroy(rt);

        try
        {
            byte[] jpg = tex.EncodeToJPG(jpegQuality);
            return new CapturedFrame
            {
                frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                imageBase64 = Convert.ToBase64String(jpg),
                width = size.x,
                height = size.y
            };
        }
        finally
        {
            Destroy(tex);
        }
    }

    private Vector2Int ResolveOutputDimensions(int sourceWidth, int sourceHeight)
    {
        if (maxImageDimension <= 0)
        {
            return new Vector2Int(sourceWidth, sourceHeight);
        }

        int longestSide = Mathf.Max(sourceWidth, sourceHeight);
        if (longestSide <= maxImageDimension)
        {
            return new Vector2Int(sourceWidth, sourceHeight);
        }

        float scale = maxImageDimension / (float)longestSide;
        return new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale)),
            Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale))
        );
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
