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
}

[Serializable]
public class PipelineResponse
{
    public string frame_id;
    public int image_width;
    public int image_height;
    public List<PipelineBlock> blocks = new List<PipelineBlock>();
    public PipelineProvider provider;
    public bool mock_used;
    public string[] warnings;
    public PipelineLatency latency_ms;
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
