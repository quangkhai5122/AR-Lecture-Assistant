using System;

public readonly struct SpeechRecognitionResult
{
    public SpeechRecognitionResult(string text, bool isFinal)
    {
        Text = text;
        IsFinal = isFinal;
    }

    public string Text { get; }
    public bool IsFinal { get; }
}

public interface ISpeechToTextService : IDisposable
{
    event Action<SpeechRecognitionResult> ResultReceived;
    event Action<string> StatusChanged;
    event Action RestartRequested;

    void StartListening();
    void StopListening();
}
