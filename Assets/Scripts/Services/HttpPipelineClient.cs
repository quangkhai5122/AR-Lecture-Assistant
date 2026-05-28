using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP client cho toàn bộ backend Flask: health, pipeline, OCR-only, translate-only.
/// Trên điện thoại, backendBaseUrl phải dùng IP LAN của laptop, ví dụ http://192.168.1.20:5000.
/// </summary>
public class HttpPipelineClient : MonoBehaviour, IPipelineClient
{
    [Header("Backend")]
    public string backendBaseUrl = "http://192.168.1.2:5000";
    public string endpointUrl = "http://127.0.0.1:5000/pipeline/frame";
    public string pipelineFramePath = "/pipeline/frame";
    public string pipelineAliasPath = "/pipeline";
    public string ocrPath = "/ocr";
    public string translatePath = "/translate";
    public string speechTranscribePath = "/speech/transcribe";
    public string speechTranslateTextPath = "/speech/translate-text";
    public string speechStreamPath = "/speech/stream";
    public string speechSummaryPath = "/speech/summarize";
    public string healthPath = "/health";
    public int timeoutSeconds = 20;

    public async Task<BackendHealthResponse> CheckHealthAsync()
    {
        string json = await SendGetAsync(BuildUrl(healthPath));
        BackendHealthResponse response = JsonUtility.FromJson<BackendHealthResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend health JSON.");
        }

        return response;
    }

    public async Task<PipelineResponse> SendFrameAsync(
        string frameId,
        string imageBase64,
        int imageWidth,
        int imageHeight,
        string targetLanguage,
        bool mock,
        string ocrProvider = "",
        string translationProvider = ""
    )
    {
        var payload = BuildFrameRequest(
            frameId,
            imageBase64,
            imageWidth,
            imageHeight,
            targetLanguage,
            mock,
            ocrProvider,
            translationProvider
        );
        return await SendPipelineRequestAsync(payload, ResolvePipelineFrameUrl());
    }

    public async Task<PipelineResponse> SendPipelineAliasAsync(
        string frameId,
        string imageBase64,
        int imageWidth,
        int imageHeight,
        string targetLanguage,
        bool mock,
        string ocrProvider = "",
        string translationProvider = ""
    )
    {
        var payload = BuildFrameRequest(
            frameId,
            imageBase64,
            imageWidth,
            imageHeight,
            targetLanguage,
            mock,
            ocrProvider,
            translationProvider
        );
        return await SendPipelineRequestAsync(payload, BuildUrl(pipelineAliasPath));
    }

    public async Task<OCRResponse> SendOcrAsync(
        string imageBase64,
        int imageWidth,
        int imageHeight,
        bool mock,
        string ocrProvider = ""
    )
    {
        string json = await SendJsonAsync(
            BuildUrl(ocrPath),
            BuildOcrJson(imageBase64, imageWidth, imageHeight, mock, ocrProvider)
        );
        OCRResponse response = JsonUtility.FromJson<OCRResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend OCR JSON.");
        }

        return response;
    }

    public async Task<TranslateResponse> SendTranslateAsync(
        List<TranslateTextItem> texts,
        string targetLanguage,
        bool mock,
        string translationProvider = ""
    )
    {
        string json = await SendJsonAsync(
            BuildUrl(translatePath),
            BuildTranslateJson(texts, targetLanguage, mock, translationProvider)
        );
        TranslateResponse response = JsonUtility.FromJson<TranslateResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend translation JSON.");
        }

        return response;
    }

    public async Task<SpeechTranscribeResponse> SendSpeechTranscribeAsync(
        string audioBase64,
        string audioEncoding,
        int sampleRateHz,
        string languageCode,
        bool mock,
        string speechProvider = ""
    )
    {
        string json = await SendJsonAsync(
            BuildUrl(speechTranscribePath),
            BuildSpeechTranscribeJson(audioBase64, audioEncoding, sampleRateHz, languageCode, mock, speechProvider)
        );
        SpeechTranscribeResponse response = JsonUtility.FromJson<SpeechTranscribeResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend speech transcript JSON.");
        }

        return response;
    }

    public async Task<SpeechTranslateTextResponse> SendSpeechTranslateTextAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string> context,
        bool mock,
        string llmProvider = ""
    )
    {
        string json = await SendJsonAsync(
            BuildUrl(speechTranslateTextPath),
            BuildSpeechTranslateTextJson(text, sourceLanguage, targetLanguage, context, mock, llmProvider)
        );
        SpeechTranslateTextResponse response = JsonUtility.FromJson<SpeechTranslateTextResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend speech translation JSON.");
        }

        return response;
    }

    public async Task<SpeechSummaryResponse> SendSpeechSummaryAsync(
        string text,
        string targetLanguage,
        bool mock,
        string llmProvider = ""
    )
    {
        string json = await SendJsonAsync(
            BuildUrl(speechSummaryPath),
            BuildSpeechSummaryJson(text, targetLanguage, mock, llmProvider)
        );
        SpeechSummaryResponse response = JsonUtility.FromJson<SpeechSummaryResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend speech summary JSON.");
        }

        return response;
    }

    public Uri BuildSpeechStreamUri()
    {
        string url = BuildUrl(speechStreamPath);
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("wss://" + url.Substring("https://".Length));
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri("ws://" + url.Substring("http://".Length));
        }

        return new Uri(url);
    }

    public PipelineResponse ComposePipelineResponse(
        string frameId,
        OCRResponse ocrResponse,
        TranslateResponse translateResponse
    )
    {
        var response = new PipelineResponse
        {
            frame_id = frameId,
            image_width = ocrResponse != null ? ocrResponse.image_width : Screen.width,
            image_height = ocrResponse != null ? ocrResponse.image_height : Screen.height,
            blocks = new List<PipelineBlock>(),
            provider = new PipelineProvider
            {
                ocr = ocrResponse?.provider?.ocr,
                translation = translateResponse?.provider?.translation
            },
            mock_used = (ocrResponse != null && ocrResponse.mock_used) ||
                        (translateResponse != null && translateResponse.mock_used),
            warnings = MergeWarnings(ocrResponse?.warnings, translateResponse?.warnings),
            latency_ms = new PipelineLatency()
        };

        Dictionary<string, TranslationBlock> translatedById = new Dictionary<string, TranslationBlock>();
        if (translateResponse?.translations != null)
        {
            foreach (TranslationBlock translation in translateResponse.translations)
            {
                if (translation != null && !string.IsNullOrEmpty(translation.id))
                {
                    translatedById[translation.id] = translation;
                }
            }
        }

        if (ocrResponse?.blocks != null)
        {
            foreach (OCRBlock block in ocrResponse.blocks)
            {
                if (block == null) continue;

                translatedById.TryGetValue(block.id, out TranslationBlock translated);
                response.blocks.Add(new PipelineBlock
                {
                    id = block.id,
                    source_text = block.text,
                    translated_text = translated != null ? translated.translated_text : block.text,
                    bbox = block.bbox,
                    confidence = block.confidence,
                    type = translated != null ? translated.type : "text",
                    style = new LabelStyle()
                });
            }
        }

        return response;
    }

    private FramePipelineRequest BuildFrameRequest(
        string frameId,
        string imageBase64,
        int imageWidth,
        int imageHeight,
        string targetLanguage,
        bool mock,
        string ocrProvider = "",
        string translationProvider = ""
    )
    {
        return new FramePipelineRequest
        {
            frame_id = frameId,
            image_base64 = imageBase64,
            target_language = targetLanguage,
            mode = "slide_translation",
            mock = mock,
            image_width = imageWidth,
            image_height = imageHeight,
            ocr_provider = ocrProvider,
            translation_provider = translationProvider
        };
    }

    private async Task<PipelineResponse> SendPipelineRequestAsync(FramePipelineRequest payload, string url)
    {
        string json = await SendJsonAsync(url, JsonUtility.ToJson(payload));
        PipelineResponse response = JsonUtility.FromJson<PipelineResponse>(json);
        if (response == null)
        {
            throw new Exception("Cannot parse backend pipeline JSON.");
        }

        return response;
    }

    private string BuildOcrJson(
        string imageBase64,
        int imageWidth,
        int imageHeight,
        bool mock,
        string ocrProvider
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.AppendFormat("\"image_base64\":\"{0}\",", EscapeJsonString(imageBase64 ?? ""));
        builder.AppendFormat("\"mock\":{0},", mock ? "true" : "false");
        builder.AppendFormat("\"image_width\":{0},", imageWidth);
        builder.AppendFormat("\"image_height\":{0}", imageHeight);
        if (!string.IsNullOrWhiteSpace(ocrProvider))
        {
            builder.AppendFormat(",\"ocr_provider\":\"{0}\"", EscapeJsonString(ocrProvider));
        }
        builder.Append("}");
        return builder.ToString();
    }

    private string BuildTranslateJson(
        List<TranslateTextItem> texts,
        string targetLanguage,
        bool mock,
        string translationProvider
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.AppendFormat("\"target_language\":\"{0}\",", EscapeJsonString(targetLanguage ?? "vi"));
        builder.AppendFormat("\"mock\":{0},", mock ? "true" : "false");
        builder.Append("\"texts\":[");

        List<TranslateTextItem> safeTexts = texts ?? new List<TranslateTextItem>();
        for (int i = 0; i < safeTexts.Count; i++)
        {
            if (i > 0) builder.Append(",");
            TranslateTextItem item = safeTexts[i];
            builder.Append("{");
            builder.AppendFormat("\"id\":\"{0}\",", EscapeJsonString(item?.id ?? ""));
            builder.AppendFormat("\"text\":\"{0}\"", EscapeJsonString(item?.text ?? ""));
            builder.Append("}");
        }

        builder.Append("]");
        if (!string.IsNullOrWhiteSpace(translationProvider))
        {
            builder.AppendFormat(",\"translation_provider\":\"{0}\"", EscapeJsonString(translationProvider));
        }
        builder.Append("}");
        return builder.ToString();
    }

    private string BuildSpeechTranscribeJson(
        string audioBase64,
        string audioEncoding,
        int sampleRateHz,
        string languageCode,
        bool mock,
        string speechProvider
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.AppendFormat("\"audio_base64\":\"{0}\",", EscapeJsonString(audioBase64 ?? ""));
        builder.AppendFormat("\"audio_encoding\":\"{0}\",", EscapeJsonString(string.IsNullOrWhiteSpace(audioEncoding) ? "LINEAR16" : audioEncoding));
        builder.AppendFormat("\"sample_rate_hz\":{0},", Mathf.Max(1, sampleRateHz));
        builder.AppendFormat("\"language_code\":\"{0}\",", EscapeJsonString(string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode));
        builder.AppendFormat("\"mock\":{0}", mock ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(speechProvider))
        {
            builder.AppendFormat(",\"speech_provider\":\"{0}\"", EscapeJsonString(speechProvider));
        }
        builder.Append("}");
        return builder.ToString();
    }

    private string BuildSpeechTranslateTextJson(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string> context,
        bool mock,
        string llmProvider
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.AppendFormat("\"text\":\"{0}\",", EscapeJsonString(text ?? ""));
        builder.AppendFormat("\"source_language\":\"{0}\",", EscapeJsonString(string.IsNullOrWhiteSpace(sourceLanguage) ? "en-US" : sourceLanguage));
        builder.AppendFormat("\"target_language\":\"{0}\",", EscapeJsonString(string.IsNullOrWhiteSpace(targetLanguage) ? "vi" : targetLanguage));
        builder.AppendFormat("\"mock\":{0},", mock ? "true" : "false");
        builder.Append("\"context\":[");
        List<string> safeContext = context ?? new List<string>();
        for (int i = 0; i < safeContext.Count; i++)
        {
            if (i > 0) builder.Append(",");
            builder.AppendFormat("\"{0}\"", EscapeJsonString(safeContext[i] ?? ""));
        }
        builder.Append("]");
        if (!string.IsNullOrWhiteSpace(llmProvider))
        {
            builder.AppendFormat(",\"llm_provider\":\"{0}\"", EscapeJsonString(llmProvider));
        }
        builder.Append("}");
        return builder.ToString();
    }

    private string BuildSpeechSummaryJson(
        string text,
        string targetLanguage,
        bool mock,
        string llmProvider
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.AppendFormat("\"text\":\"{0}\",", EscapeJsonString(text ?? ""));
        builder.AppendFormat("\"target_language\":\"{0}\",", EscapeJsonString(string.IsNullOrWhiteSpace(targetLanguage) ? "vi" : targetLanguage));
        builder.AppendFormat("\"mock\":{0}", mock ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(llmProvider))
        {
            builder.AppendFormat(",\"llm_provider\":\"{0}\"", EscapeJsonString(llmProvider));
        }
        builder.Append("}");
        return builder.ToString();
    }

    private string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private async Task<string> SendGetAsync(string url)
    {
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            ThrowIfRequestFailed(request);
            return request.downloadHandler.text;
        }
    }

    private async Task<string> SendJsonAsync(string url, string json)
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            ThrowIfRequestFailed(request);
            return request.downloadHandler.text;
        }
    }

    private void ThrowIfRequestFailed(UnityWebRequest request)
    {
#if UNITY_2020_2_OR_NEWER
        bool hasError = request.result != UnityWebRequest.Result.Success;
#else
        bool hasError = request.isNetworkError || request.isHttpError;
#endif
        if (hasError)
        {
            throw new Exception($"Backend request failed: {request.error}\n{request.downloadHandler.text}");
        }
    }

    private string BuildUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return backendBaseUrl;
        }

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string baseUrl = string.IsNullOrWhiteSpace(backendBaseUrl)
            ? "http://127.0.0.1:5000"
            : backendBaseUrl.TrimEnd('/');
        string normalizedPath = path.StartsWith("/") ? path : "/" + path;
        return baseUrl + normalizedPath;
    }

    private string ResolvePipelineFrameUrl()
    {
        bool hasCustomEndpoint = !string.IsNullOrWhiteSpace(endpointUrl) &&
                                 endpointUrl != "http://127.0.0.1:5000/pipeline/frame";
        return hasCustomEndpoint ? endpointUrl : BuildUrl(pipelineFramePath);
    }

    private string[] MergeWarnings(string[] first, string[] second)
    {
        List<string> warnings = new List<string>();
        if (first != null) warnings.AddRange(first);
        if (second != null) warnings.AddRange(second);
        return warnings.ToArray();
    }
}
