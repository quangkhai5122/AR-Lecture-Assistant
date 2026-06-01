using System.Collections.Generic;
using System.Threading.Tasks;
using ARLectureTranslator.Models;
using UnityEngine;

namespace ARLectureTranslator.Services
{
    /// <summary>
    /// Mock client để Unity/AR/UI phát triển độc lập mà không cần backend, OCR hay dịch thật.
    /// TODO(MVP): Khi backend ổn định, dùng HttpPipelineClient trong production demo.
    /// </summary>
    public class MockPipelineClient : IPipelineClient
    {
        public Task<PipelineResponse> SendFrameAsync(
            string frameId,
            string imageBase64,
            int imageWidth,
            int imageHeight,
            string targetLanguage,
            bool mock
        )
        {
            int w = imageWidth > 0 ? imageWidth : Screen.width;
            int h = imageHeight > 0 ? imageHeight : Screen.height;

            var response = new PipelineResponse
            {
                frame_id = frameId,
                image_width = w,
                image_height = h,
                blocks = new List<PipelineBlock>
                {
                    new PipelineBlock
                    {
                        id = "mock_1",
                        source_text = "Deep learning uses neural networks.",
                        translated_text = "Học sâu sử dụng mạng nơ-ron.",
                        bbox = new float[] { w * 0.18f, h * 0.28f, w * 0.82f, h * 0.36f },
                        confidence = 1.0f,
                        type = "text",
                        style = new LabelStyle { font_size = 38, background_alpha = 0.68f }
                    },
                    new PipelineBlock
                    {
                        id = "mock_2",
                        source_text = "L = -Σ y log(p)",
                        translated_text = "L = -Σ y log(p)",
                        bbox = new float[] { w * 0.25f, h * 0.45f, w * 0.75f, h * 0.53f },
                        confidence = 1.0f,
                        type = "formula",
                        style = new LabelStyle { font_size = 34, background_alpha = 0.55f }
                    }
                }
            };

            return Task.FromResult(response);
        }
    }
}
