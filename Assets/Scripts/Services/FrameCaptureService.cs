using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public struct CapturedFrame
{
    public string frameId;
    public string imageBase64;
    public int width;
    public int height;
    public string captureSource;
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
    public FrameCaptureSource captureSource = FrameCaptureSource.Screenshot;
    [SerializeField] private ARCameraManager arCameraManager;

    [Tooltip("Cố chọn AR camera configuration có độ phân giải cao nhất trước khi capture OCR.")]
    [SerializeField] private bool preferHighestCameraResolution = true;

    [Tooltip("Trong Auto mode, bỏ qua AR CPU image nếu cạnh dài vẫn quá thấp và fallback sang screenshot.")]
    [Range(0, 4096)]
    [SerializeField] private int minRawCameraLongSide = 1280;

    [Header("Upload Image")]
    [SerializeField] private FrameImageEncoding imageEncoding = FrameImageEncoding.Jpeg;

    [Tooltip("Hide screen-space UI while taking screenshots. Enabling this can cause visible blink during Translate.")]
    [SerializeField] private bool hideScreenSpaceCanvasesForScreenshot = false;

    [Tooltip("Mask visible app UI in the captured screenshot before sending OCR. This avoids UI text without hiding canvases on screen.")]
    [SerializeField] private bool maskScreenSpaceUiForScreenshot = true;

    [SerializeField] private Color screenshotUiMaskColor = new Color(0.54f, 0.56f, 0.58f, 1f);

    [Range(10, 95)]
    public int jpegQuality = 92;

    [Tooltip("Giới hạn cạnh dài nhất của ảnh gửi backend. 2560 giữ chi tiết chữ nhỏ tốt hơn 1280 mà vẫn tránh payload quá lớn. Đặt 0 để tắt resize.")]
    [Range(0, 4096)]
    public int maxImageDimension = 3072;

    [Tooltip("Một số thiết bị cần lật ảnh CPU camera theo trục Y để khớp texture Unity.")]
    public bool mirrorCameraImageY = true;

    private bool cameraConfigurationApplied;

    public string LastCaptureSource { get; private set; } = "none";
    public string LastCaptureWarning { get; private set; } = string.Empty;

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
        LastCaptureSource = "none";
        LastCaptureWarning = string.Empty;

        // Do not change AR camera configuration here; it can restart the camera feed during Translate.
        // Bước 1: Thử AR Camera Raw (không cần WaitForEndOfFrame)
        if (captureSource != FrameCaptureSource.Screenshot &&
            TryCaptureARCameraRaw(out CapturedFrame cameraFrame))
        {
            LastCaptureSource = "ar_camera_raw";
            Debug.Log($"[FrameCaptureService] Captured frame via ar_camera_raw ({cameraFrame.width}x{cameraFrame.height}).");
            tcs.TrySetResult(cameraFrame);
            yield break;
        }

        if (captureSource == FrameCaptureSource.ARCameraRaw)
        {
            LastCaptureWarning = "AR camera CPU image is unavailable.";
            Debug.LogWarning("[FrameCaptureService] " + LastCaptureWarning);
            tcs.TrySetException(new InvalidOperationException("Cannot acquire AR camera CPU image."));
            yield break;
        }

        if (captureSource == FrameCaptureSource.Auto)
        {
            LastCaptureWarning = "AR camera CPU image unavailable; using screenshot fallback.";
            Debug.LogWarning("[FrameCaptureService] " + LastCaptureWarning);
        }

        // Bước 2: Screenshot — BẮT BUỘC phải đợi WaitForEndOfFrame trên Android
        List<Rect> uiMaskRects = !hideScreenSpaceCanvasesForScreenshot && maskScreenSpaceUiForScreenshot
            ? CollectScreenSpaceUiRects()
            : null;
        List<Canvas> hiddenCanvases = hideScreenSpaceCanvasesForScreenshot
            ? DisableScreenSpaceCanvasesForCapture()
            : null;
        yield return new WaitForEndOfFrame();

        try
        {
            CapturedFrame frame = CaptureScreenshotFrame(uiMaskRects);
            LastCaptureSource = "screenshot";
            Debug.Log($"[FrameCaptureService] Captured frame via screenshot ({frame.width}x{frame.height}).");
            tcs.TrySetResult(frame);
        }
        catch (Exception)
        {
            // Bước 3: Fallback — đọc pixels từ Camera.main
            try
            {
                CapturedFrame frame = CaptureCameraReadPixels();
                LastCaptureSource = "camera_read_pixels";
                LastCaptureWarning = "Screenshot capture failed; used Camera.main ReadPixels fallback.";
                Debug.LogWarning("[FrameCaptureService] " + LastCaptureWarning);
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
                    height = cameraTexture.height,
                    captureSource = "ar_camera_raw"
                };
                return true;
            }
            finally
            {
                Destroy(cameraTexture);
            }
        }
    }

    private CapturedFrame CaptureScreenshotFrame(List<Rect> uiMaskRects)
    {
        Texture2D capturedTexture = ScreenCapture.CaptureScreenshotAsTexture();
        if (capturedTexture == null)
        {
            throw new InvalidOperationException("Screenshot returned null.");
        }

        MaskScreenshotRects(capturedTexture, uiMaskRects);
        Texture2D uploadTexture = ResizeIfNeeded(capturedTexture);
        try
        {
            byte[] encodedImage = EncodeForUpload(uploadTexture);
            return new CapturedFrame
            {
                frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                imageBase64 = Convert.ToBase64String(encodedImage),
                width = uploadTexture.width,
                height = uploadTexture.height,
                captureSource = "screenshot"
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
                height = size.y,
                captureSource = "camera_read_pixels"
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

    private List<Rect> CollectScreenSpaceUiRects()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        var rects = new List<Rect>();
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null ||
                !canvas.enabled ||
                !canvas.gameObject.activeInHierarchy ||
                canvas.renderMode == RenderMode.WorldSpace)
            {
                continue;
            }

            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(false);
            foreach (Graphic graphic in graphics)
            {
                if (graphic == null ||
                    !graphic.enabled ||
                    !graphic.gameObject.activeInHierarchy ||
                    graphic.canvasRenderer.GetAlpha() <= 0.01f ||
                    graphic.color.a <= 0.01f)
                {
                    continue;
                }

                RectTransform rectTransform = graphic.rectTransform;
                if (rectTransform == null)
                {
                    continue;
                }

                Rect screenRect = GetScreenRect(rectTransform, canvasCamera);
                if (screenRect.width <= 1f || screenRect.height <= 1f)
                {
                    continue;
                }

                rects.Add(ClampScreenRect(screenRect));
            }
        }

        return rects;
    }

    private Rect GetScreenRect(RectTransform rectTransform, Camera canvasCamera)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[i]);
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private Rect ClampScreenRect(Rect rect)
    {
        float minX = Mathf.Clamp(Mathf.Floor(rect.xMin), 0f, Screen.width);
        float minY = Mathf.Clamp(Mathf.Floor(rect.yMin), 0f, Screen.height);
        float maxX = Mathf.Clamp(Mathf.Ceil(rect.xMax), 0f, Screen.width);
        float maxY = Mathf.Clamp(Mathf.Ceil(rect.yMax), 0f, Screen.height);
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private void MaskScreenshotRects(Texture2D texture, List<Rect> screenRects)
    {
        if (texture == null || screenRects == null || screenRects.Count == 0)
        {
            return;
        }

        float scaleX = texture.width / Mathf.Max(1f, Screen.width);
        float scaleY = texture.height / Mathf.Max(1f, Screen.height);
        Color[] fillPixels = null;
        int fillPixelCount = 0;

        foreach (Rect screenRect in screenRects)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(screenRect.xMin * scaleX), 0, texture.width);
            int y = Mathf.Clamp(Mathf.FloorToInt(screenRect.yMin * scaleY), 0, texture.height);
            int width = Mathf.Clamp(Mathf.CeilToInt(screenRect.width * scaleX), 0, texture.width - x);
            int height = Mathf.Clamp(Mathf.CeilToInt(screenRect.height * scaleY), 0, texture.height - y);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            int requiredPixels = width * height;
            if (fillPixels == null || fillPixelCount != requiredPixels)
            {
                fillPixels = new Color[requiredPixels];
                for (int i = 0; i < fillPixels.Length; i++)
                {
                    fillPixels[i] = screenshotUiMaskColor;
                }
                fillPixelCount = requiredPixels;
            }

            texture.SetPixels(x, y, width, height, fillPixels);
        }

        texture.Apply(false);
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
