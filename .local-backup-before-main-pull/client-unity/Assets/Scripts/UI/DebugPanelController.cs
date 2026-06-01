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
            sb.AppendLine($"blocks: {response.blocks?.Count ?? 0}");

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
