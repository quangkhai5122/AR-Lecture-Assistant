using System.Text;
using ARLectureTranslator.Models;
using TMPro;
using UnityEngine;

namespace ARLectureTranslator.UI
{
    public class DebugPanelController : MonoBehaviour
    {
        public TMP_Text debugText;

        public void ShowResponse(PipelineResponse response)
        {
            if (debugText == null || response == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"frame: {response.frame_id}");
            sb.AppendLine($"image: {response.image_width}x{response.image_height}");
            sb.AppendLine($"mock_used: {response.mock_used}");
            if (response.provider != null)
            {
                sb.AppendLine($"provider: ocr={response.provider.ocr}, translation={response.provider.translation}");
            }
            if (response.latency_ms != null)
            {
                sb.AppendLine($"latency: ocr={response.latency_ms.ocr:0.0}ms, translation={response.latency_ms.translation:0.0}ms, total={response.latency_ms.total:0.0}ms");
            }
            sb.AppendLine($"blocks: {response.blocks?.Count ?? 0}");
            if (response.warnings != null && response.warnings.Length > 0)
            {
                sb.AppendLine("warnings:");
                foreach (var warning in response.warnings)
                {
                    sb.AppendLine($"  - {warning}");
                }
            }

            if (response.blocks != null)
            {
                foreach (var block in response.blocks)
                {
                    sb.AppendLine($"- {block.id} [{block.type}] conf={block.confidence:0.00}");
                    sb.AppendLine($"  src: {block.source_text}");
                    sb.AppendLine($"  vi : {block.translated_text}");
                }
            }

            debugText.text = sb.ToString();
        }

        public void Clear()
        {
            if (debugText != null) debugText.text = "";
        }
    }
}
