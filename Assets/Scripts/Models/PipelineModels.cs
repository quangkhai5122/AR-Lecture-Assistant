using System;
using System.Collections.Generic;

/// <summary>
/// Contract Unity dùng để gọi backend /pipeline/frame.
/// Tên field giữ snake_case để khớp JSON Flask.
/// </summary>
[Serializable]
public class FramePipelineRequest
{
    public string frame_id;
    public string image_base64;
    public string target_language = "vi";
    public string mode = "slide_translation";
    public bool mock = false;
    public int image_width;
    public int image_height;
    public string ocr_provider;
    public string translation_provider;
}

[Serializable]
public class OCRRequest
{
    public string image_base64;
    public bool mock = false;
    public int image_width;
    public int image_height;
    public string ocr_provider;
}

[Serializable]
public class TranslateRequest
{
    public string target_language = "vi";
    public bool mock = false;
    public List<TranslateTextItem> texts = new List<TranslateTextItem>();
    public string translation_provider;
}

[Serializable]
public class TranslateTextItem
{
    public string id;
    public string text;
}

[Serializable]
public class SpeechTranscribeResponse
{
    public string transcript;
    public string language_code;
    public float confidence;
    public SpeechProvider provider;
    public bool mock_used;
    public string[] warnings;
}

[Serializable]
public class SpeechTranslateTextResponse
{
    public string source_text;
    public string translated_text;
    public string source_language;
    public string target_language;
    public SpeechProvider provider;
    public string model;
    public bool mock_used;
    public string[] warnings;
}

[Serializable]
public class SpeechSummaryResponse
{
    public string summary_text;
    public string target_language;
    public SpeechProvider provider;
    public string model;
    public bool mock_used;
    public string[] warnings;
}

[Serializable]
public class SpeechStreamMessage
{
    public string type;
    public string transcript;
    public bool is_final;
    public float confidence;
    public float stability;
    public string provider;
    public string error;
}

[Serializable]
public class SpeechProvider
{
    public string speech;
    public string llm;
}

[Serializable]
public class BackendHealthResponse
{
    public string status;
    public string service;
    public string mode;
    public PipelineProvider provider;
}

[Serializable]
public class PipelineResponse
{
    public string frame_id;
    public int image_width;
    public int image_height;
    public DocumentSurface document_surface;
    public List<PipelineBlock> blocks = new List<PipelineBlock>();
    public PipelineProvider provider;
    public bool mock_used;
    public string[] warnings;
    public PipelineLatency latency_ms;
}

[Serializable]
public class DocumentSurface
{
    public float[] corners;
    public float confidence;
    public string method;
    public string source;
}

[Serializable]
public class OCRResponse
{
    public int image_width;
    public int image_height;
    public List<OCRBlock> blocks = new List<OCRBlock>();
    public PipelineProvider provider;
    public bool mock_used;
    public string[] warnings;
}

[Serializable]
public class OCRBlock
{
    public string id;
    public string text;
    public float[] bbox;
    public float confidence;
}

[Serializable]
public class TranslateResponse
{
    public List<TranslationBlock> translations = new List<TranslationBlock>();
    public PipelineProvider provider;
    public bool mock_used;
    public string[] warnings;
}

[Serializable]
public class TranslationBlock
{
    public string id;
    public string source_text;
    public string translated_text;
    public string type;
}

[Serializable]
public class PipelineBlock
{
    public string id;
    public string source_text;
    public string translated_text;
    public float[] bbox;
    public float confidence;
    public string type;
    public LabelStyle style;
}

[Serializable]
public class LabelStyle
{
    public int font_size = 36;
    public float background_alpha = 0.65f;
}

[Serializable]
public class PipelineProvider
{
    public string ocr;
    public string translation;
}

[Serializable]
public class PipelineLatency
{
    public float ocr;
    public float translation;
    public float total;
}
