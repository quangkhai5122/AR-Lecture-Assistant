// MockOCRService.cs
// Đặt tại: Assets/Scripts/Services/MockOCRService.cs
// Mục đích: Giả lập OCR - nhận diện text trên slide/bảng

using System.Threading.Tasks;
using UnityEngine;

public class MockOCRService : MonoBehaviour
{
    [SerializeField] private float mockDelaySeconds = 1.0f;

    // Danh sách text mẫu giả lập OCR từ slide
    private string[] mockSlideTexts = new string[]
    {
        "Introduction to Machine Learning",
        "Supervised Learning: Classification and Regression",
        "Neural Networks consist of layers of interconnected nodes",
        "Data Preprocessing is an essential step in any ML pipeline",
        "Overfitting occurs when a model learns noise instead of signal",
        "Cross-validation helps evaluate model performance",
        "Gradient Descent is an optimization algorithm",
        "Feature Engineering improves model accuracy"
    };

    // Danh sách text mẫu giả lập lời nói giáo viên (cho subtitle)
    private string[] mockSpeechTexts = new string[]
    {
        "Today we will learn about machine learning basics",
        "Please look at the slide, this is very important",
        "Let me explain this concept with an example",
        "Any questions so far?",
        "Now let's move on to the next topic",
        "This algorithm is widely used in industry",
        "Remember to review this for the exam"
    };

    private int slideIndex = 0;
    private int speechIndex = 0;

    /// <summary>
    /// Giả lập OCR: "đọc" text từ slide/bảng
    /// Mỗi lần gọi sẽ trả về text mẫu khác nhau
    /// </summary>
    public async Task<string> RecognizeTextFromSlideAsync()
    {
        // Giả lập delay xử lý OCR
        await Task.Delay((int)(mockDelaySeconds * 1000));

        string result = mockSlideTexts[slideIndex % mockSlideTexts.Length];
        slideIndex++;

        Debug.Log($"[MockOCR] Slide text: {result}");
        return result;
    }

    /// <summary>
    /// Giả lập Speech-to-Text: "nghe" lời giáo viên
    /// Dùng cho subtitle
    /// </summary>
    public async Task<string> RecognizeSpeechAsync()
    {
        await Task.Delay((int)(mockDelaySeconds * 500));

        string result = mockSpeechTexts[speechIndex % mockSpeechTexts.Length];
        speechIndex++;

        Debug.Log($"[MockOCR] Speech text: {result}");
        return result;
    }

    /// <summary>
    /// Reset index về 0 (khi nhấn Clear)
    /// </summary>
    public void Reset()
    {
        slideIndex = 0;
        speechIndex = 0;
    }
}