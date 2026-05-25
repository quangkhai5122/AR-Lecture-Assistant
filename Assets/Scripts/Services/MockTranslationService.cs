// MockTranslationService.cs
using System.Threading.Tasks;
using UnityEngine;

public class MockTranslationService : MonoBehaviour, ITranslationService
{
    [SerializeField] private float mockDelaySeconds = 1.5f;

    public async Task<TranslationResult> TranslateAsync(string sourceText)
    {
        // Giả lập delay mạng
        await Task.Delay((int)(mockDelaySeconds * 1000));

        return new TranslationResult
        {
            OriginalText = sourceText,
            TranslatedText = "[VN] Đây là bản dịch mẫu của: "
                             + sourceText,
            SourceLanguage = "en",
            TargetLanguage = "vi"
        };
    }
}