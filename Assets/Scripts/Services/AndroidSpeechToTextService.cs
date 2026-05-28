using System;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
public sealed class AndroidSpeechToTextService : ISpeechToTextService
{
    private const string RecognizeSpeechAction = "android.speech.action.RECOGNIZE_SPEECH";
    private const string LanguageModelExtra = "android.speech.extra.LANGUAGE_MODEL";
    private const string FreeFormLanguageModel = "free_form";
    private const string LanguageExtra = "android.speech.extra.LANGUAGE";
    private const string PartialResultsExtra = "android.speech.extra.PARTIAL_RESULTS";
    private const string MaxResultsExtra = "android.speech.extra.MAX_RESULTS";
    private const string ResultsRecognition = "results_recognition";

    private readonly string language;

    private AndroidJavaObject activity;
    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject recognizerIntent;
    private RecognitionListener recognitionListener;
    private bool shouldContinue;
    private bool isStarting;
    private bool isDisposed;

    public AndroidSpeechToTextService(string language)
    {
        this.language = string.IsNullOrWhiteSpace(language) ? "vi-VN" : language.Trim();
    }

    public event Action<SpeechRecognitionResult> ResultReceived;
    public event Action<string> StatusChanged;
    public event Action RestartRequested;

    public void StartListening()
    {
        if (isDisposed) return;

        shouldContinue = true;
        EnsureRecognizer();
        StartRecognizer();
    }

    public void StopListening()
    {
        shouldContinue = false;

        AndroidJavaObject recognizer = speechRecognizer;
        if (recognizer == null) return;

        RunOnUiThread(() =>
        {
            try
            {
                recognizer.Call("stopListening");
                recognizer.Call("cancel");
            }
            catch (Exception ex)
            {
                EmitStatus("Speech stop failed: " + ex.Message);
            }
        });
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        shouldContinue = false;

        AndroidJavaObject recognizerToDestroy = speechRecognizer;
        speechRecognizer = null;

        if (recognizerToDestroy != null)
        {
            RunOnUiThread(() =>
            {
                try
                {
                    recognizerToDestroy.Call("destroy");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AndroidSpeechToText] Destroy failed: " + ex.Message);
                }
                finally
                {
                    recognizerToDestroy.Dispose();
                }
            });
        }

        recognizerIntent?.Dispose();
        activity?.Dispose();
        recognizerIntent = null;
        activity = null;
    }

    private void EnsureRecognizer()
    {
        if (speechRecognizer != null) return;

        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        using (var speechRecognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
        {
            bool isAvailable = speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", activity);
            if (!isAvailable)
            {
                EmitStatus("Android speech recognition is not available on this device.");
                return;
            }

            speechRecognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
        }

        recognizerIntent = new AndroidJavaObject("android.content.Intent", RecognizeSpeechAction);
        recognizerIntent.Call<AndroidJavaObject>("putExtra", LanguageModelExtra, FreeFormLanguageModel);
        recognizerIntent.Call<AndroidJavaObject>("putExtra", LanguageExtra, language);
        recognizerIntent.Call<AndroidJavaObject>("putExtra", PartialResultsExtra, true);
        recognizerIntent.Call<AndroidJavaObject>("putExtra", MaxResultsExtra, 3);

        recognitionListener = new RecognitionListener(this);
        speechRecognizer.Call("setRecognitionListener", recognitionListener);
    }

    private void StartRecognizer()
    {
        AndroidJavaObject recognizer = speechRecognizer;
        AndroidJavaObject intent = recognizerIntent;
        if (!shouldContinue || isDisposed || isStarting || recognizer == null || intent == null)
        {
            return;
        }

        isStarting = true;
        RunOnUiThread(() =>
        {
            try
            {
                recognizer.Call("startListening", intent);
                EmitStatus("Listening");
            }
            catch (Exception ex)
            {
                EmitStatus("Speech start failed: " + ex.Message);
                RequestRestart();
            }
            finally
            {
                isStarting = false;
            }
        });
    }

    private void RequestRestart()
    {
        if (!shouldContinue || isDisposed) return;
        RestartRequested?.Invoke();
    }

    private void RunOnUiThread(Action action)
    {
        if (activity == null || action == null) return;
        activity.Call("runOnUiThread", new AndroidJavaRunnable(action));
    }

    private void EmitResult(string text, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        ResultReceived?.Invoke(new SpeechRecognitionResult(text.Trim(), isFinal));
    }

    private void EmitStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return;
        StatusChanged?.Invoke(status.Trim());
    }

    private static string ReadBestResult(AndroidJavaObject bundle)
    {
        if (bundle == null) return string.Empty;

        AndroidJavaObject matches = null;
        try
        {
            matches = bundle.Call<AndroidJavaObject>("getStringArrayList", ResultsRecognition);
            if (matches == null) return string.Empty;

            int count = matches.Call<int>("size");
            if (count <= 0) return string.Empty;

            return matches.Call<string>("get", 0);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AndroidSpeechToText] Could not read recognition result: " + ex.Message);
            return string.Empty;
        }
        finally
        {
            matches?.Dispose();
        }
    }

    private sealed class RecognitionListener : AndroidJavaProxy
    {
        private readonly AndroidSpeechToTextService owner;

        public RecognitionListener(AndroidSpeechToTextService owner)
            : base("android.speech.RecognitionListener")
        {
            this.owner = owner;
        }

        public void onReadyForSpeech(AndroidJavaObject parameters)
        {
            owner.EmitStatus("Ready for speech");
        }

        public void onBeginningOfSpeech()
        {
            owner.EmitStatus("Speech detected");
        }

        public void onRmsChanged(float rmsdB)
        {
        }

        public void onBufferReceived(byte[] buffer)
        {
        }

        public void onEndOfSpeech()
        {
            owner.EmitStatus("Processing speech");
        }

        public void onError(int error)
        {
            owner.EmitStatus("Speech recognizer error " + error);
            owner.RequestRestart();
        }

        public void onResults(AndroidJavaObject results)
        {
            owner.EmitResult(ReadBestResult(results), true);
            owner.RequestRestart();
        }

        public void onPartialResults(AndroidJavaObject partialResults)
        {
            owner.EmitResult(ReadBestResult(partialResults), false);
        }

        public void onEvent(int eventType, AndroidJavaObject parameters)
        {
        }
    }
}
#endif
