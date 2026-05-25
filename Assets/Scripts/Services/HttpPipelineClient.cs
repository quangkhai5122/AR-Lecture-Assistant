using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP client gọi backend Flask /pipeline/frame để chạy OCR + Translate.
/// Trên điện thoại, endpointUrl phải dùng IP LAN của laptop, ví dụ http://192.168.1.20:5000/pipeline/frame.
/// </summary>
public class HttpPipelineClient : MonoBehaviour, IPipelineClient
{
    [Header("Backend")]
    public string endpointUrl = "http://127.0.0.1:5000/pipeline/frame";
    public int timeoutSeconds = 20;

    public async Task<PipelineResponse> SendFrameAsync(
        string frameId,
        string imageBase64,
        int imageWidth,
        int imageHeight,
        string targetLanguage,
        bool mock
    )
    {
        var payload = new FramePipelineRequest
        {
            frame_id = frameId,
            image_base64 = imageBase64,
            target_language = targetLanguage,
            mode = "slide_translation",
            mock = mock,
            image_width = imageWidth,
            image_height = imageHeight
        };

        string json = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(endpointUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            bool hasError = request.result != UnityWebRequest.Result.Success;
#else
            bool hasError = request.isNetworkError || request.isHttpError;
#endif
            if (hasError)
            {
                throw new Exception($"Backend request failed: {request.error}\n{request.downloadHandler.text}");
            }

            PipelineResponse response = JsonUtility.FromJson<PipelineResponse>(request.downloadHandler.text);
            if (response == null)
            {
                throw new Exception("Cannot parse backend response JSON.");
            }

            return response;
        }
    }
}
