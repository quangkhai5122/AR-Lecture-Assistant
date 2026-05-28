using System;

public sealed class TranscriptEntry
{
    public TranscriptEntry(DateTime timestamp, string text)
        : this(timestamp, text, string.Empty, false)
    {
    }

    public TranscriptEntry(DateTime timestamp, string text, string translatedText, bool translationPending)
    {
        Timestamp = timestamp;
        Text = text;
        TranslatedText = translatedText;
        TranslationPending = translationPending;
    }

    public DateTime Timestamp { get; }
    public string Text { get; }
    public string TranslatedText { get; private set; }
    public bool TranslationPending { get; private set; }

    public void SetTranslation(string translatedText)
    {
        TranslatedText = translatedText ?? string.Empty;
        TranslationPending = false;
    }
}
