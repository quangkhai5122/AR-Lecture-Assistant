using System;
using System.Collections;
using System.Collections.Generic;
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

public enum FrameImageEncoding
{
    Png,
    Jpeg
}

/// <summary>
/// Chụp frame camera thành PNG/JPG base64 để gửi backend OCR/Translate.
/// Auto ưu tiên ARCameraManager.TryAcquireLatestCpuImage để tránh dính UI overlay,
/// rồi fallback về screenshot nếu thiết bị/Editor chưa cấp CPU image.
/// </summary>
public class FrameCaptureService : MonoBehaviour
{
    [Header("Capture Source")]
    public FrameCaptureSource captureSource = FrameCaptureSource.Auto;
    [SerializeField] private ARCameraManager arCameraManager;

    [Tooltip("Cố chọn AR camera configuration có độ phân giải cao nhất trước khi capture OCR.")]
    [SerializeField] private bool preferHighestCameraResolution = true;

    [Tooltip("Trong Auto mode, bỏ qua AR CPU image nếu cạnh dài vẫn quá thấp và fallback sang screenshot.")]
    [Range(0, 4096)]
    [SerializeField] private int minRawCameraLongSide = 1600;

    [Header("Upload Image")]
    [SerializeField] private FrameImageEncoding imageEncoding = FrameImageEncoding.Png;

    [Tooltip("Khi phải fallback sang screenshot, ẩn UI screen-space trong đúng frame capture để OCR không đọc nhầm nút/overlay.")]
    [SerializeField] private bool hideScreenSpaceCanvasesForScreenshot = true;

    [Range(10, 95)]
    public int jpegQuality = 90;

    [Tooltip("Giới hạn cạnh dài nhất của ảnh gửi backend. 2560 giữ chi tiết chữ nhỏ tốt hơn 1280 mà vẫn tránh payload quá lớn. Đặt 0 để tắt resize.")]
    [Range(0, 4096)]
    public int maxImageDimension = 2560;

    [Tooltip("Một số thiết bị cần lật ảnh CPU camera theo trục Y để khớp texture Unity.")]
    public bool mirrorCameraImageY = true;

    private bool cameraConfigurationApplied;

    private void OnEnable()
    {
        cameraConfigurationApplied = false;
        TryApplyBestCameraConfiguration();
    }

    public Task<CapturedFrame> CaptureAsync()
    {
        var tcs = new TaskCompletionSource<CapturedFrame>();
        StartCoroutine(CaptureCoroutine(tcs));
        return tcs.Task;
    }

    private IEnumerator CaptureCoroutine(TaskCompletionSource<CapturedFrame> tcs)
    {
        TryApplyBestCameraConfiguration();

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
        List<Canvas> hiddenCanvases = hideScreenSpaceCanvasesForScreenshot
            ? DisableScreenSpaceCanvasesForCapture()
            : null;
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
        finally
        {
            RestoreScreenSpaceCanvases(hiddenCanvases);
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
            if (captureSource == FrameCaptureSource.Auto &&
                minRawCameraLongSide > 0 &&
                Mathf.Max(outputDimensions.x, outputDimensions.y) < minRawCameraLongSide)
            {
                return false;
            }

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

                byte[] encodedImage = EncodeForUpload(cameraTexture);
                frame = new CapturedFrame
                {
                    frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                    imageBase64 = Convert.ToBase64String(encodedImage),
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
            byte[] encodedImage = EncodeForUpload(uploadTexture);
            return new CapturedFrame
            {
                frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                imageBase64 = Convert.ToBase64String(encodedImage),
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
            byte[] encodedImage = EncodeForUpload(tex);
            return new CapturedFrame
            {
                frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                imageBase64 = Convert.ToBase64String(encodedImage),
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

    private byte[] EncodeForUpload(Texture2D texture)
    {
        return imageEncoding == FrameImageEncoding.Png
            ? texture.EncodeToPNG()
            : texture.EncodeToJPG(jpegQuality);
    }

    private List<Canvas> DisableScreenSpaceCanvasesForCapture()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        var hidden = new List<Canvas>();
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.enabled || canvas.renderMode == RenderMode.WorldSpace)
            {
                continue;
            }

            canvas.enabled = false;
            hidden.Add(canvas);
        }

        return hidden;
    }

    private void RestoreScreenSpaceCanvases(List<Canvas> canvases)
    {
        if (canvases == null) return;

        foreach (Canvas canvas in canvases)
        {
            if (canvas != null)
            {
                canvas.enabled = true;
            }
        }
    }

    private void TryApplyBestCameraConfiguration()
    {
        if (!preferHighestCameraResolution || cameraConfigurationApplied)
        {
            return;
        }

        if (arCameraManager == null)
        {
            arCameraManager = FindAnyObjectByType<ARCameraManager>();
        }

        if (arCameraManager == null || arCameraManager.subsystem == null)
        {
            return;
        }

        NativeArray<XRCameraConfiguration> configurations = default;
        try
        {
            configurations = arCameraManager.GetConfigurations(Allocator.Temp);
            if (!configurations.IsCreated || configurations.Length == 0)
            {
                return;
            }

            int bestIndex = 0;
            int bestPixels = -1;
            int bestFrameRate = -1;
            for (int i = 0; i < configurations.Length; i++)
            {
                XRCameraConfiguration configuration = configurations[i];
                Vector2Int resolution = configuration.resolution;
                int pixels = resolution.x * resolution.y;
                int frameRate = configuration.framerate.GetValueOrDefault(0);

                if (pixels > bestPixels || (pixels == bestPixels && frameRate > bestFrameRate))
                {
                    bestIndex = i;
                    bestPixels = pixels;
                    bestFrameRate = frameRate;
                }
            }

            XRCameraConfiguration bestConfiguration = configurations[bestIndex];
            XRCameraConfiguration? currentConfiguration = arCameraManager.currentConfiguration;
            if (!currentConfiguration.HasValue || !currentConfiguration.Value.Equals(bestConfiguration))
            {
                arCameraManager.currentConfiguration = bestConfiguration;
                Vector2Int resolution = bestConfiguration.resolution;
                Debug.Log($"[FrameCaptureService] Requested AR camera resolution {resolution.x}x{resolution.y} for OCR capture.");
            }

            cameraConfigurationApplied = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FrameCaptureService] Cannot apply high resolution AR camera configuration: {ex.Message}");
        }
        finally
        {
            if (configurations.IsCreated)
            {
                configurations.Dispose();
            }
        }
    }
}
