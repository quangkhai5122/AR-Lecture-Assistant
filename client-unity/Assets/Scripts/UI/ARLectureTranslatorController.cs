using System;
using System.Threading.Tasks;
using ARLectureTranslator.AR;
using ARLectureTranslator.Models;
using ARLectureTranslator.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ARLectureTranslator.UI
{
    /// <summary>
    /// Controller chính nối UI -> capture frame -> pipeline client -> AR label placer.
    /// </summary>
    public class ARLectureTranslatorController : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("Bật để không cần backend. Unity sẽ dùng MockPipelineClient.")]
        public bool useMockClient = true;

        [Tooltip("Khi gọi backend, bật mock=true để backend trả OCR/dịch giả ổn định.")]
        public bool backendMockMode = true;

        public string targetLanguage = "vi";

        [Header("Services")]
        public FrameCaptureService frameCaptureService;
        public HttpPipelineClient httpPipelineClient;
        public ARLabelPlacer labelPlacer;
        public DebugPanelController debugPanel;

        [Header("UI")]
        public TMP_Text statusText;
        public Button scanButton;
        public Button clearButton;

        private IPipelineClient pipelineClient;
        private bool isBusy;

        private void Awake()
        {
            if (useMockClient)
            {
                pipelineClient = new MockPipelineClient();
            }
            else
            {
                pipelineClient = httpPipelineClient;
            }
        }

        private void Start()
        {
            ApplySimpleButtonStyle();
            SetStatus(useMockClient
                ? "Sẵn sàng: Unity mock mode. Hãy quét mặt phẳng rồi bấm Quét."
                : "Sẵn sàng: Backend mode. Hãy kiểm tra endpoint URL.");
        }

        public async void OnScanButtonClicked()
        {
            await ScanAndPlaceAsync();
        }

        public void OnClearButtonClicked()
        {
            labelPlacer?.ClearLabels();
            debugPanel?.Clear();
            SetStatus("Đã xóa label.");
        }

        public async Task ScanAndPlaceAsync()
        {
            if (isBusy) return;
            isBusy = true;
            SetButtonsInteractable(false);

            try
            {
                SetStatus("Đang capture frame...");

                CapturedFrame frame;
                if (frameCaptureService != null && !useMockClient)
                {
                    frame = await frameCaptureService.CaptureAsync();
                }
                else
                {
                    frame = new CapturedFrame
                    {
                        frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
                        imageBase64 = "",
                        width = Screen.width,
                        height = Screen.height
                    };
                }

                SetStatus("Đang OCR + dịch...");
                PipelineResponse response = await pipelineClient.SendFrameAsync(
                    frame.frameId,
                    frame.imageBase64,
                    frame.width,
                    frame.height,
                    targetLanguage,
                    backendMockMode
                );

                debugPanel?.ShowResponse(response);

                SetStatus("Đang neo bản dịch vào AR plane...");
                int placed = labelPlacer != null ? await labelPlacer.PlaceLabelsAsync(response) : 0;

                if (placed == 0)
                {
                    SetStatus("Chưa đặt được label. Hãy lia camera chậm qua bảng/slide để nhận diện plane rồi bấm Quét lại.");
                }
                else
                {
                    SetStatus($"Đã đặt {placed} label dịch.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus($"Lỗi: {ex.Message}");
            }
            finally
            {
                isBusy = false;
                SetButtonsInteractable(true);
            }
        }

        private void SetStatus(string message)
        {
            Debug.Log(message);
            if (statusText != null) statusText.text = message;
        }

        private void SetButtonsInteractable(bool value)
        {
            if (scanButton != null) scanButton.interactable = value;
            if (clearButton != null) clearButton.interactable = value;
        }

        private void ApplySimpleButtonStyle()
        {
            StyleButton(scanButton, "Quét", Color.black, Color.white);
            StyleButton(clearButton, "Xóa", Color.white, Color.black);
        }

        private static void StyleButton(Button button, string label, Color background, Color foreground)
        {
            if (button == null) return;

            Image image = button.GetComponent<Image>();
            if (image != null) image.color = background;

            ColorBlock colors = button.colors;
            bool isWhite = background == Color.white;
            colors.normalColor = background;
            colors.highlightedColor = isWhite ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f);
            colors.pressedColor = isWhite ? new Color(0.82f, 0.82f, 0.82f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            button.colors = colors;

            foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = label;
                text.color = foreground;
            }

            foreach (Text text in button.GetComponentsInChildren<Text>(true))
            {
                text.text = label;
                text.color = foreground;
            }
        }
    }
}
