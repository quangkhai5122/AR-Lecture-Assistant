using System;
using System.Collections.Generic;

namespace ARLectureTranslator.Models
{
    /// <summary>
    /// Request gửi từ Unity tới backend /pipeline/frame.
    /// Contract phải khớp contracts/pipeline_request.schema.json.
    /// </summary>
    [Serializable]
    public class FramePipelineRequest
    {
        public string frame_id;
        public string image_base64;
        public string target_language = "vi";
        public string mode = "slide_translation";
        public bool mock = false;
    }

    /// <summary>
    /// Response nhận từ backend.
    /// Contract phải khớp contracts/pipeline_response.schema.json.
    /// </summary>
    [Serializable]
    public class PipelineResponse
    {
        public string frame_id;
        public int image_width;
        public int image_height;
        public List<PipelineBlock> blocks = new List<PipelineBlock>();
    }

    [Serializable]
    public class PipelineBlock
    {
        public string id;
        public string source_text;
        public string translated_text;

        // bbox = [x1, y1, x2, y2], gốc tọa độ top-left theo ảnh gửi lên backend.
        public float[] bbox;

        public float confidence;

        // text | formula | mixed
        public string type;

        public LabelStyle style;
    }

    [Serializable]
    public class LabelStyle
    {
        public int font_size = 36;
        public float background_alpha = 0.65f;
    }
}
