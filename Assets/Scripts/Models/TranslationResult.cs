// TranslationResult.cs
// Đặt tại: Assets/Scripts/Models/TranslationResult.cs

[System.Serializable]
public class TranslationResult
{
    public string OriginalText;
    public string TranslatedText;
    public string SourceLanguage;
    public string TargetLanguage;

    public TranslationResult()
    {
        SourceLanguage = "en";
        TargetLanguage = "vi";
    }

    public TranslationResult(string original, string translated)
    {
        OriginalText = original;
        TranslatedText = translated;
        SourceLanguage = "en";
        TargetLanguage = "vi";
    }
}