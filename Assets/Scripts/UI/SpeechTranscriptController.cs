using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

[DisallowMultipleComponent]
public class SpeechTranscriptController : MonoBehaviour
{
    [Header("Backend Speech Translation")]
    [SerializeField] private bool useBackendSpeechTranslation = true;
    [SerializeField] private bool useStreamingSpeech = false;
    [SerializeField] private bool useLowLatencySpeechDefaults = true;
    [SerializeField] private bool speechBackendMockMode = false;
    [SerializeField] private HttpPipelineClient httpPipelineClient;
    [SerializeField] private string speechProvider = "google";
    [SerializeField] private string llmProvider = "gemini";
    [SerializeField] private string targetLanguage = "vi";
    [SerializeField] private int microphoneSampleRateHz = 16000;
    [SerializeField] private float audioChunkSeconds = 1.5f;
    [SerializeField] private float microphoneStartupTimeoutSeconds = 1f;
    [SerializeField] private int streamingSendIntervalMs = 250;
    [SerializeField] private float minSpeechRms = 0.003f;
    [SerializeField] private float microphoneReadIntervalSeconds = 0.12f;
    [SerializeField] private float speechEndSilenceSeconds = 1.15f;
    [SerializeField] private float minUtteranceSeconds = 0.45f;
    [SerializeField] private float maxUtteranceSeconds = 18f;

    [Header("Speech Recognition")]
    [SerializeField] private bool showTranscriptUi = false;
    [SerializeField] private bool autoStartListening = false;
    [SerializeField] private string recognitionLanguage = "en-US";
    [SerializeField] private float rollingWindowSeconds = 45f;
    [SerializeField] private float uiRefreshIntervalSeconds = 0.15f;
    [SerializeField] private float androidRestartDelaySeconds = 0.35f;
    [SerializeField] private bool commitOnlyCompleteSentences = true;
    [SerializeField] private float sentenceSilenceCommitSeconds = 1.4f;
    [SerializeField] private bool useMockInEditorOrUnsupported = true;

    [Header("Notes")]
    [SerializeField] private string notesFileName = "lecture_notes.md";

    private readonly List<TranscriptEntry> transcriptEntries = new List<TranscriptEntry>();
    private readonly Queue<SpeechRecognitionResult> pendingResults = new Queue<SpeechRecognitionResult>();
    private readonly Queue<string> pendingStatuses = new Queue<string>();
    private readonly object queueLock = new object();
    private int pendingRestartCount;

    private readonly string[] mockPhrases =
    {
        "Today we will review the main idea from the previous slide.",
        "This definition is important because it appears in later examples.",
        "Notice how the input changes before the model returns a prediction.",
        "You should add this step to your notes for the exercise.",
        "The next section connects the formula with a practical workflow. "
    };

    private ISpeechToTextService speechService;
    private LectureNotesService notesService;
    private GameObject modalRoot;
    private Button toggleButton;
    private Button addToNoteButton;
    private Button viewNotesButton;
    private Button exportNotesButton;
    private Button deleteNotesButton;
    private Button summarizeButton;
    private Button closeButton;
    private TextMeshProUGUI transcriptText;
    private TextMeshProUGUI statusText;
    private ScrollRect transcriptScroll;
    private Coroutine backendSpeechCoroutine;
    private ClientWebSocket speechStreamSocket;
    private CancellationTokenSource speechStreamCts;
    private Task speechStreamReceiveTask;
    private bool showingNotes;
    private bool showingSummary;
    private string summaryText = string.Empty;
    private string pendingSentenceText = string.Empty;
    private DateTime pendingSentenceTimestamp;
    private float pendingSentenceLastUpdateAt = -1f;
    private string interimText = string.Empty;
    private DateTime interimTimestamp;
    private bool isListening;
    private bool usingMockProvider;
    private bool uiDirty = true;
    private bool waitingForPermission;
    private int mockPhraseIndex;
    private float nextMockPhraseAt;
    private float nextUiRefreshAt;
    private float restartSpeechAt = -1f;

    private void Start()
    {
        ApplyLowLatencySpeechDefaults();
        notesService = new LectureNotesService(notesFileName);
        if (showTranscriptUi && toggleButton == null)
        {
            BuildUi();
        }

        if (autoStartListening)
        {
            Debug.Log("[SpeechTranscript] autoStartListening is ignored; press Transcript to start listening.");
        }

        SetStatus("Transcript ready");
        UpdateTranscriptToggleLabel();
    }

    public void EnsureTranscriptUiVisible()
    {
        showTranscriptUi = true;

        if (toggleButton != null)
        {
            toggleButton.gameObject.SetActive(true);
            return;
        }

        BuildUi();
    }

    public void HideTranscriptUi()
    {
        showTranscriptUi = false;

        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }

        if (toggleButton != null)
        {
            toggleButton.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        CheckDeferredPermission();
        TickMockProvider();
        DrainPendingEvents();
        CheckScheduledRestart();
        CheckPendingSentenceTimeout();
        PruneExpiredEntries();

        if (uiDirty && Time.unscaledTime >= nextUiRefreshAt)
        {
            RefreshTranscriptText();
            nextUiRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, uiRefreshIntervalSeconds);
        }
    }

    private void OnDestroy()
    {
        if (toggleButton != null) toggleButton.onClick.RemoveListener(OnTranscriptTogglePressed);
        if (addToNoteButton != null) addToNoteButton.onClick.RemoveListener(AddCurrentTranscriptToNote);
        if (viewNotesButton != null) viewNotesButton.onClick.RemoveListener(ToggleNotesView);
        if (exportNotesButton != null) exportNotesButton.onClick.RemoveListener(ExportNotes);
        if (deleteNotesButton != null) deleteNotesButton.onClick.RemoveListener(DeleteNotes);
        if (summarizeButton != null) summarizeButton.onClick.RemoveListener(OnSummarizePressed);
        if (closeButton != null) closeButton.onClick.RemoveListener(HideModal);

        StopListening();
        speechService?.Dispose();
        speechService = null;
    }

    public void StartListening()
    {
        if (isListening) return;

        isListening = true;
        waitingForPermission = false;
        UpdateTranscriptToggleLabel();
        uiDirty = true;

        if (useBackendSpeechTranslation)
        {
            StartBackendSpeechTranslation();
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            waitingForPermission = true;
            Permission.RequestUserPermission(Permission.Microphone);
            SetStatus("Waiting for microphone permission");
            return;
        }

        StartAndroidProvider();
#else
        StartMockProvider();
#endif
    }

    public void StopListening()
    {
        bool wasListening = isListening;
        isListening = false;
        usingMockProvider = false;
        waitingForPermission = false;
        restartSpeechAt = -1f;
        if (backendSpeechCoroutine != null)
        {
            StopCoroutine(backendSpeechCoroutine);
            backendSpeechCoroutine = null;
        }
        StopSpeechStream();
        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }
        speechService?.StopListening();

        if (wasListening)
        {
            SetStatus("Transcript paused");
        }

        UpdateTranscriptToggleLabel();
        uiDirty = true;
    }

    private void StartBackendSpeechTranslation()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            waitingForPermission = true;
            Permission.RequestUserPermission(Permission.Microphone);
            SetStatus("Waiting for microphone permission");
            return;
        }
#endif

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            SetStatus("No microphone device found");
            return;
        }

        if (httpPipelineClient == null)
        {
            httpPipelineClient = FindAnyObjectByType<HttpPipelineClient>();
        }

        if (httpPipelineClient == null)
        {
            httpPipelineClient = gameObject.AddComponent<HttpPipelineClient>();
        }

        if (backendSpeechCoroutine != null)
        {
            StopCoroutine(backendSpeechCoroutine);
        }

        backendSpeechCoroutine = useStreamingSpeech && !speechBackendMockMode
            ? StartCoroutine(CaptureStreamingSpeechLoop())
            : StartCoroutine(CaptureAndTranslateSpeechLoop());
        SetStatus(speechBackendMockMode ? "Mock backend speech active" : "Google STT + Gemini active");
    }

    private void CheckDeferredPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!waitingForPermission || !isListening) return;

        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            waitingForPermission = false;
            if (useBackendSpeechTranslation)
            {
                StartBackendSpeechTranslation();
            }
            else
            {
                StartAndroidProvider();
            }
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void StartAndroidProvider()
    {
        usingMockProvider = false;

        if (speechService == null)
        {
            speechService = new AndroidSpeechToTextService(recognitionLanguage);
            speechService.ResultReceived += EnqueueSpeechResult;
            speechService.StatusChanged += EnqueueStatus;
            speechService.RestartRequested += EnqueueRestart;
        }

        speechService.StartListening();
        SetStatus("Speech service starting");
    }
#endif

    private void StartMockProvider()
    {
        if (!useMockInEditorOrUnsupported)
        {
            SetStatus("Speech recognition is unavailable on this platform");
            return;
        }

        usingMockProvider = true;
        nextMockPhraseAt = Time.unscaledTime + 0.6f;
        SetStatus("Mock transcript active");
    }

    private void TickMockProvider()
    {
        if (!isListening || !usingMockProvider || Time.unscaledTime < nextMockPhraseAt) return;

        string phrase = mockPhrases[mockPhraseIndex % mockPhrases.Length];
        mockPhraseIndex++;
        HandleSpeechResult(new SpeechRecognitionResult(phrase, true));
        nextMockPhraseAt = Time.unscaledTime + 1.6f;
    }

    private void ApplyLowLatencySpeechDefaults()
    {
        if (!useLowLatencySpeechDefaults || useStreamingSpeech || speechBackendMockMode)
        {
            return;
        }

        audioChunkSeconds = Mathf.Clamp(audioChunkSeconds, 0.75f, 1.5f);
        microphoneStartupTimeoutSeconds = Mathf.Clamp(microphoneStartupTimeoutSeconds, 0.2f, 1f);
        minSpeechRms = Mathf.Min(minSpeechRms, 0.003f);
        microphoneReadIntervalSeconds = Mathf.Clamp(microphoneReadIntervalSeconds, 0.08f, 0.2f);
        speechEndSilenceSeconds = Mathf.Clamp(speechEndSilenceSeconds, 0.95f, 1.6f);
        minUtteranceSeconds = Mathf.Clamp(minUtteranceSeconds, 0.25f, 0.8f);
        maxUtteranceSeconds = Mathf.Max(maxUtteranceSeconds, 12f);
        rollingWindowSeconds = Mathf.Max(rollingWindowSeconds, 45f);
        uiRefreshIntervalSeconds = Mathf.Min(uiRefreshIntervalSeconds, 0.15f);
        sentenceSilenceCommitSeconds = Mathf.Clamp(sentenceSilenceCommitSeconds, 1.2f, 2.2f);
    }

    private System.Collections.IEnumerator CaptureStreamingSpeechLoop()
    {
        speechStreamCts = new CancellationTokenSource();
        CancellationToken token = speechStreamCts.Token;
        Task openTask = OpenSpeechStreamAsync(token);
        while (!openTask.IsCompleted)
        {
            yield return null;
        }

        if (openTask.IsFaulted || speechStreamSocket == null || speechStreamSocket.State != WebSocketState.Open)
        {
            string message = openTask.Exception != null && openTask.Exception.InnerException != null
                ? openTask.Exception.InnerException.Message
                : "Could not open speech stream";
            SetStatus(message);
            backendSpeechCoroutine = null;
            yield break;
        }

        string deviceName = ResolveMicrophoneDeviceName();
        int sampleRateHz = ResolveMicrophoneSampleRate(deviceName);
        int clipLengthSeconds = Mathf.Max(2, Mathf.CeilToInt(audioChunkSeconds * 3f));
        AudioClip clip = Microphone.Start(deviceName, true, clipLengthSeconds, sampleRateHz);
        int readPosition = 0;
        float sendInterval = Mathf.Max(0.05f, streamingSendIntervalMs / 1000f);
        SetStatus("Streaming Google STT");

        while (isListening && useBackendSpeechTranslation && useStreamingSpeech && !token.IsCancellationRequested)
        {
            yield return new WaitForSeconds(sendInterval);

            if (speechStreamSocket == null || speechStreamSocket.State != WebSocketState.Open)
            {
                break;
            }

            int currentPosition = Microphone.GetPosition(deviceName);
            float[] samples = ReadMicrophoneSamples(clip, ref readPosition, currentPosition);
            if (samples.Length == 0 || CalculateRms(samples) < minSpeechRms)
            {
                continue;
            }

            byte[] bytes = EncodePcm16Bytes(samples, Mathf.Max(1, clip.channels));
            Task sendTask = SendSpeechStreamBytesAsync(bytes, token);
            while (!sendTask.IsCompleted)
            {
                yield return null;
            }

            if (sendTask.IsFaulted)
            {
                string message = sendTask.Exception != null && sendTask.Exception.InnerException != null
                    ? sendTask.Exception.InnerException.Message
                    : "Speech stream send failed";
                SetStatus(message);
                break;
            }
        }

        if (Microphone.IsRecording(deviceName))
        {
            Microphone.End(deviceName);
        }

        StopSpeechStream();
        backendSpeechCoroutine = null;
    }

    private async Task OpenSpeechStreamAsync(CancellationToken token)
    {
        speechStreamSocket = new ClientWebSocket();
        await speechStreamSocket.ConnectAsync(httpPipelineClient.BuildSpeechStreamUri(), token);
        await SendSpeechStreamStringAsync(BuildStreamConfigJson(), token);
        speechStreamReceiveTask = ReceiveSpeechStreamLoopAsync(speechStreamSocket, token);
    }

    private async Task ReceiveSpeechStreamLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using (var stream = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    string json = Encoding.UTF8.GetString(stream.ToArray());
                    SpeechStreamMessage message = JsonUtility.FromJson<SpeechStreamMessage>(json);
                    if (message == null) continue;

                    if (message.type == "result" && !string.IsNullOrWhiteSpace(message.transcript))
                    {
                        EnqueueSpeechResult(new SpeechRecognitionResult(message.transcript, message.is_final));
                    }
                    else if (message.type == "error")
                    {
                        EnqueueStatus(string.IsNullOrWhiteSpace(message.error) ? "Speech stream error" : message.error);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EnqueueStatus("Speech stream receive failed: " + ex.Message);
        }
    }

    private void StopSpeechStream()
    {
        try
        {
            speechStreamCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        ClientWebSocket socket = speechStreamSocket;
        speechStreamSocket = null;
        speechStreamReceiveTask = null;

        if (socket != null)
        {
            try
            {
                socket.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        speechStreamCts?.Dispose();
        speechStreamCts = null;
    }

    private async Task SendSpeechStreamStringAsync(string value, CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        await speechStreamSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private async Task SendSpeechStreamBytesAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes == null || bytes.Length == 0) return;
        await speechStreamSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, token);
    }

    private string BuildStreamConfigJson()
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.AppendFormat("\"audio_encoding\":\"LINEAR16\",");
        builder.AppendFormat("\"sample_rate_hz\":{0},", Mathf.Max(1, microphoneSampleRateHz));
        builder.AppendFormat("\"language_code\":\"{0}\",", EscapeJsonString(string.IsNullOrWhiteSpace(recognitionLanguage) ? "en-US" : recognitionLanguage));
        builder.AppendFormat("\"interim_results\":true,");
        builder.AppendFormat("\"mock\":{0},", speechBackendMockMode ? "true" : "false");
        builder.AppendFormat("\"speech_provider\":\"{0}\"", EscapeJsonString(string.IsNullOrWhiteSpace(speechProvider) ? "google" : speechProvider));
        builder.Append("}");
        return builder.ToString();
    }

    private System.Collections.IEnumerator CaptureAndTranslateSpeechLoop()
    {
        string deviceName = ResolveMicrophoneDeviceName();
        int sampleRateHz = ResolveMicrophoneSampleRate(deviceName);

        if (speechBackendMockMode)
        {
            yield return CaptureMockBackendSpeechLoop(sampleRateHz);
            backendSpeechCoroutine = null;
            yield break;
        }

        int clipLengthSeconds = Mathf.Max(30, Mathf.CeilToInt(maxUtteranceSeconds) + 8);
        AudioClip clip = Microphone.Start(deviceName, true, clipLengthSeconds, sampleRateHz);
        if (clip == null)
        {
            SetStatus("Microphone did not start");
            backendSpeechCoroutine = null;
            yield break;
        }

        float waitUntil = Time.unscaledTime + Mathf.Max(0.2f, microphoneStartupTimeoutSeconds);
        while (isListening && Microphone.GetPosition(deviceName) <= 0 && Time.unscaledTime < waitUntil)
        {
            yield return null;
        }

        if (isListening && Microphone.GetPosition(deviceName) <= 0)
        {
            SetStatus("Microphone did not start");
            if (Microphone.IsRecording(deviceName))
            {
                Microphone.End(deviceName);
            }
            backendSpeechCoroutine = null;
            yield break;
        }

        int channels = clip != null ? Mathf.Max(1, clip.channels) : 1;
        int readPosition = Microphone.GetPosition(deviceName);
        var utteranceSamples = new List<float>(Mathf.Max(1, sampleRateHz * channels * 4));
        bool hasSpeech = false;
        float lastSpeechAt = -1f;
        float nextIdleStatusAt = 0f;
        float readInterval = Mathf.Clamp(microphoneReadIntervalSeconds, 0.06f, 0.25f);
        float endSilence = Mathf.Max(0.6f, speechEndSilenceSeconds);

        SetStatus("Listening");

        while (isListening && useBackendSpeechTranslation)
        {
            yield return new WaitForSeconds(readInterval);

            if (!Microphone.IsRecording(deviceName))
            {
                SetStatus("Microphone stopped");
                break;
            }

            int currentPosition = Microphone.GetPosition(deviceName);
            float[] samples = ReadMicrophoneSamples(clip, ref readPosition, currentPosition);
            if (samples.Length == 0)
            {
                continue;
            }

            bool voiceDetected = CalculateRms(samples) >= minSpeechRms;
            float now = Time.unscaledTime;

            if (voiceDetected)
            {
                if (!hasSpeech)
                {
                    utteranceSamples.Clear();
                    SetStatus("Listening to sentence");
                }

                hasSpeech = true;
                lastSpeechAt = now;
                AppendSamples(utteranceSamples, samples);
            }
            else if (hasSpeech)
            {
                AppendSamples(utteranceSamples, samples);
            }
            else if (now >= nextIdleStatusAt)
            {
                SetStatus("Listening");
                nextIdleStatusAt = now + 1f;
            }

            if (!hasSpeech)
            {
                continue;
            }

            float utteranceSeconds = utteranceSamples.Count / (float)Mathf.Max(1, sampleRateHz * channels);
            bool hasMinimumAudio = utteranceSeconds >= Mathf.Max(0.1f, minUtteranceSeconds);
            bool endedBySilence = !voiceDetected && lastSpeechAt > 0f && now - lastSpeechAt >= endSilence;
            bool endedByLength = utteranceSeconds >= Mathf.Max(2f, maxUtteranceSeconds);
            if (!hasMinimumAudio || (!endedBySilence && !endedByLength))
            {
                continue;
            }

            float[] utterance = utteranceSamples.ToArray();
            utteranceSamples.Clear();
            hasSpeech = false;
            lastSpeechAt = -1f;

            SetStatus(endedByLength ? "Transcribing long sentence" : "Transcribing sentence");
            Task task = ProcessAudioChunkAsync(utterance, channels, sampleRateHz);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                string message = task.Exception != null && task.Exception.InnerException != null
                    ? task.Exception.InnerException.Message
                    : "Speech backend request failed";
                Debug.LogWarning("[SpeechTranscript] " + message);
                SetStatus(message);
                yield return new WaitForSeconds(0.8f);
            }
            else
            {
                SetStatus("Listening");
            }
        }

        if (Microphone.IsRecording(deviceName))
        {
            Microphone.End(deviceName);
        }

        backendSpeechCoroutine = null;
    }

    private System.Collections.IEnumerator CaptureMockBackendSpeechLoop(int sampleRateHz)
    {
        while (isListening && useBackendSpeechTranslation && speechBackendMockMode)
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, audioChunkSeconds));

            if (!isListening) break;

            Task task = ProcessAudioChunkAsync(new float[0], 1, sampleRateHz);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                string message = task.Exception != null && task.Exception.InnerException != null
                    ? task.Exception.InnerException.Message
                    : "Speech backend request failed";
                Debug.LogWarning("[SpeechTranscript] " + message);
                SetStatus(message);
                yield return new WaitForSeconds(0.8f);
            }
        }
    }

    private async Task ProcessAudioChunkAsync(float[] samples, int channels, int sampleRateHz)
    {
        string audioBase64 = speechBackendMockMode ? "" : EncodePcm16Base64(samples, channels);
        SpeechTranscribeResponse response = await httpPipelineClient.SendSpeechTranscribeAsync(
            audioBase64,
            "LINEAR16",
            Mathf.Max(1, sampleRateHz),
            recognitionLanguage,
            speechBackendMockMode,
            speechProvider
        );

        if (response != null && !string.IsNullOrWhiteSpace(response.transcript))
        {
            HandleSpeechResult(new SpeechRecognitionResult(response.transcript, true));
            SetStatus("Transcript received");
        }
    }

    private void EnqueueSpeechResult(SpeechRecognitionResult result)
    {
        lock (queueLock)
        {
            pendingResults.Enqueue(result);
        }
    }

    private void EnqueueStatus(string status)
    {
        lock (queueLock)
        {
            pendingStatuses.Enqueue(status);
        }
    }

    private void EnqueueRestart()
    {
        lock (queueLock)
        {
            pendingRestartCount++;
        }
    }

    private void DrainPendingEvents()
    {
        while (true)
        {
            SpeechRecognitionResult result;
            lock (queueLock)
            {
                if (pendingResults.Count == 0) break;
                result = pendingResults.Dequeue();
            }

            HandleSpeechResult(result);
        }

        while (true)
        {
            string status;
            lock (queueLock)
            {
                if (pendingStatuses.Count == 0) break;
                status = pendingStatuses.Dequeue();
            }

            SetStatus(status);
        }

        int restarts;
        lock (queueLock)
        {
            restarts = pendingRestartCount;
            pendingRestartCount = 0;
        }

        if (restarts > 0 && isListening && !waitingForPermission)
        {
            restartSpeechAt = Time.unscaledTime + Mathf.Max(0.1f, androidRestartDelaySeconds);
        }
    }

    private void CheckScheduledRestart()
    {
        if (restartSpeechAt < 0f || !isListening || waitingForPermission) return;
        if (Time.unscaledTime < restartSpeechAt) return;

        restartSpeechAt = -1f;
        speechService?.StartListening();
    }

    private void HandleSpeechResult(SpeechRecognitionResult result)
    {
        if (!isListening) return;

        string text = NormalizeWhitespace(result.Text);
        if (string.IsNullOrWhiteSpace(text)) return;

        DateTime now = DateTime.Now;
        if (result.IsFinal)
        {
            if (commitOnlyCompleteSentences)
            {
                AddFinalFragmentToPendingSentence(text, now);
            }
            else if (!IsDuplicateCommittedSentence(text, now))
            {
                AddCommittedSentence(now, text);
            }

            interimText = string.Empty;
        }
        else
        {
            interimText = text;
            interimTimestamp = now;
        }

        uiDirty = true;
    }

    private void AddFinalFragmentToPendingSentence(string text, DateTime now)
    {
        if (IsDuplicateCommittedSentence(text, now)) return;
        if (IsDuplicatePendingSentence(text)) return;

        if (string.IsNullOrWhiteSpace(pendingSentenceText))
        {
            pendingSentenceTimestamp = now;
            pendingSentenceText = text;
        }
        else
        {
            pendingSentenceText = JoinSentenceFragments(pendingSentenceText, text);
        }

        pendingSentenceLastUpdateAt = Time.unscaledTime;

        if (EndsWithSentenceTerminator(pendingSentenceText))
        {
            CommitPendingSentence();
        }
    }

    private void CheckPendingSentenceTimeout()
    {
        if (string.IsNullOrWhiteSpace(pendingSentenceText)) return;
        if (pendingSentenceLastUpdateAt < 0f) return;

        float timeout = Mathf.Max(0.2f, sentenceSilenceCommitSeconds);
        if (Time.unscaledTime - pendingSentenceLastUpdateAt >= timeout)
        {
            CommitPendingSentence();
        }
    }

    private void CommitPendingSentence()
    {
        string text = NormalizeWhitespace(pendingSentenceText);
        if (!string.IsNullOrWhiteSpace(text) && !IsDuplicateCommittedSentence(text, DateTime.Now))
        {
            AddCommittedSentence(pendingSentenceTimestamp == default(DateTime) ? DateTime.Now : pendingSentenceTimestamp, text);
        }

        pendingSentenceText = string.Empty;
        pendingSentenceLastUpdateAt = -1f;
        uiDirty = true;
    }

    private void AddCommittedSentence(DateTime timestamp, string sourceText)
    {
        bool shouldTranslate = useBackendSpeechTranslation && httpPipelineClient != null;
        var entry = new TranscriptEntry(timestamp, sourceText, string.Empty, shouldTranslate);
        transcriptEntries.Add(entry);
        uiDirty = true;

        if (shouldTranslate)
        {
            _ = TranslateEntryAsync(entry);
        }
    }

    private async Task TranslateEntryAsync(TranscriptEntry entry)
    {
        try
        {
            SpeechTranslateTextResponse response = await httpPipelineClient.SendSpeechTranslateTextAsync(
                entry.Text,
                recognitionLanguage,
                targetLanguage,
                BuildContextForTranslation(entry),
                speechBackendMockMode,
                llmProvider
            );

            entry.SetTranslation(response != null ? response.translated_text : string.Empty);
            SetStatus("Translated sentence");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SpeechTranscript] Translation failed: " + ex.Message);
            entry.SetTranslation("[translation error] " + ex.Message);
            SetStatus("Speech translation failed");
        }

        uiDirty = true;
    }

    private List<string> BuildContextForTranslation(TranscriptEntry currentEntry)
    {
        var context = new List<string>();
        for (int i = Mathf.Max(0, transcriptEntries.Count - 6); i < transcriptEntries.Count; i++)
        {
            TranscriptEntry entry = transcriptEntries[i];
            if (entry == null || entry == currentEntry || string.IsNullOrWhiteSpace(entry.Text)) continue;
            context.Add(entry.Text);
        }

        return context;
    }

    private bool IsDuplicateCommittedSentence(string text, DateTime now)
    {
        if (transcriptEntries.Count == 0) return false;

        TranscriptEntry last = transcriptEntries[transcriptEntries.Count - 1];
        return string.Equals(last.Text, text, StringComparison.OrdinalIgnoreCase) &&
               (now - last.Timestamp).TotalSeconds < 2.0;
    }

    private bool IsDuplicatePendingSentence(string text)
    {
        return string.Equals(pendingSentenceText, text, StringComparison.OrdinalIgnoreCase);
    }

    private void PruneExpiredEntries()
    {
        DateTime cutoff = DateTime.Now.AddSeconds(-Mathf.Max(1f, rollingWindowSeconds));
        int removed = transcriptEntries.RemoveAll(entry => entry.Timestamp < cutoff);
        if (removed > 0) uiDirty = true;

        if (!string.IsNullOrWhiteSpace(interimText) &&
            (DateTime.Now - interimTimestamp).TotalSeconds > Mathf.Max(1f, rollingWindowSeconds))
        {
            interimText = string.Empty;
            uiDirty = true;
        }
    }

    private void BuildUi()
    {
        if (toggleButton != null) return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("TranscriptCanvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        Transform parent = canvas.transform;
        toggleButton = CreateButton("TranscriptToggleButton", parent, "Transcript", new Color(0.08f, 0.58f, 0.78f, 0.94f));
        RectTransform toggleRect = toggleButton.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(1f, 1f);
        toggleRect.anchorMax = new Vector2(1f, 1f);
        toggleRect.pivot = new Vector2(1f, 1f);
        toggleRect.anchoredPosition = new Vector2(-16f, -70f);
        toggleRect.sizeDelta = new Vector2(160f, 50f);
        toggleButton.onClick.AddListener(OnTranscriptTogglePressed);

        modalRoot = new GameObject("TranscriptModal");
        modalRoot.transform.SetParent(parent, false);
        RectTransform modalRect = modalRoot.AddComponent<RectTransform>();
        modalRect.anchorMin = new Vector2(0.05f, 0.18f);
        modalRect.anchorMax = new Vector2(0.95f, 0.78f);
        modalRect.offsetMin = Vector2.zero;
        modalRect.offsetMax = Vector2.zero;

        Image modalImage = modalRoot.AddComponent<Image>();
        modalImage.color = new Color(0.025f, 0.030f, 0.042f, 0.92f);
        Shadow modalShadow = modalRoot.AddComponent<Shadow>();
        modalShadow.effectColor = new Color(0f, 0f, 0f, 0.36f);
        modalShadow.effectDistance = new Vector2(0f, -5f);

        BuildHeader(modalRoot.transform);
        BuildScrollArea(modalRoot.transform);
        BuildActionRow(modalRoot.transform);

        modalRoot.SetActive(false);
        RefreshTranscriptText();
    }

    private void BuildHeader(Transform parent)
    {
        string titleText = "Live transcript (last " + Mathf.RoundToInt(Mathf.Max(1f, rollingWindowSeconds)) + "s)";
        TextMeshProUGUI title = CreateText("TranscriptTitle", parent, titleText, 30f, FontStyles.Bold);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(24f, -72f);
        titleRect.offsetMax = new Vector2(-92f, -18f);
        title.alignment = TextAlignmentOptions.Left;

        closeButton = CreateButton("TranscriptCloseButton", parent, "X", new Color(0.16f, 0.18f, 0.22f, 0.96f));
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-20f, -18f);
        closeRect.sizeDelta = new Vector2(56f, 56f);
        closeButton.onClick.AddListener(HideModal);

        statusText = CreateText("TranscriptStatus", parent, "Transcript ready", 20f, FontStyles.Normal);
        RectTransform statusRect = statusText.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.offsetMin = new Vector2(24f, -112f);
        statusRect.offsetMax = new Vector2(-24f, -76f);
        statusText.color = new Color(0.72f, 0.8f, 0.9f, 1f);
        statusText.fontSizeMax = 22f;
        statusText.alignment = TextAlignmentOptions.Left;
    }

    private void BuildScrollArea(Transform parent)
    {
        GameObject scrollObject = new GameObject("TranscriptScroll");
        scrollObject.transform.SetParent(parent, false);
        RectTransform scrollRectTransform = scrollObject.AddComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0f, 0.17f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(24f, 18f);
        scrollRectTransform.offsetMax = new Vector2(-24f, -122f);

        Image scrollImage = scrollObject.AddComponent<Image>();
        scrollImage.color = new Color(0.01f, 0.014f, 0.02f, 0.72f);

        transcriptScroll = scrollObject.AddComponent<ScrollRect>();
        transcriptScroll.horizontal = false;
        transcriptScroll.vertical = true;
        transcriptScroll.movementType = ScrollRect.MovementType.Clamped;
        transcriptScroll.scrollSensitivity = 28f;

        GameObject viewportObject = new GameObject("Viewport");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewportRect = viewportObject.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(14f, 12f);
        viewportRect.offsetMax = new Vector2(-14f, -12f);
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        Mask viewportMask = viewportObject.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        GameObject contentObject = new GameObject("TranscriptContent");
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform contentRect = contentObject.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        transcriptText = contentObject.AddComponent<TextMeshProUGUI>();
        transcriptText.text = string.Empty;
        transcriptText.color = Color.white;
        transcriptText.fontSize = 24f;
        transcriptText.enableWordWrapping = true;
        transcriptText.overflowMode = TextOverflowModes.Overflow;
        transcriptText.alignment = TextAlignmentOptions.TopLeft;
        transcriptText.raycastTarget = false;

        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        transcriptScroll.viewport = viewportRect;
        transcriptScroll.content = contentRect;
    }

    private void BuildActionRow(Transform parent)
    {
        GameObject rowObject = new GameObject("TranscriptActionRow");
        rowObject.transform.SetParent(parent, false);
        RectTransform rowRect = rowObject.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.offsetMin = new Vector2(24f, 20f);
        rowRect.offsetMax = new Vector2(-24f, 92f);

        HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        addToNoteButton = CreateButton("AddTranscriptToNoteButton", rowObject.transform, "Add to note", new Color(0.10f, 0.70f, 0.55f, 0.96f));
        addToNoteButton.onClick.AddListener(AddCurrentTranscriptToNote);

        viewNotesButton = CreateButton("ViewNotesButton", rowObject.transform, "Notes", new Color(0.10f, 0.44f, 0.78f, 0.96f));
        viewNotesButton.onClick.AddListener(ToggleNotesView);

        exportNotesButton = CreateButton("ExportNotesButton", rowObject.transform, "Export", new Color(0.13f, 0.16f, 0.22f, 0.96f));
        exportNotesButton.onClick.AddListener(ExportNotes);

        deleteNotesButton = CreateButton("DeleteNotesButton", rowObject.transform, "Delete", new Color(0.82f, 0.24f, 0.30f, 0.96f));
        deleteNotesButton.onClick.AddListener(DeleteNotes);

        summarizeButton = CreateButton("SummarizeTranscriptButton", rowObject.transform, "AI summary", new Color(0.38f, 0.34f, 0.95f, 0.86f));
        summarizeButton.onClick.AddListener(OnSummarizePressed);
        summarizeButton.interactable = true;
    }

    private Button CreateButton(string name, Transform parent, string label, Color color)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180f, 58f);

        bool isCloseButton = name.ToLowerInvariant().Contains("close");
        Color buttonColor = isCloseButton ? Color.white : color;
        Color textColor = isCloseButton ? Color.black : Color.white;

        Image image = buttonObject.AddComponent<Image>();
        image.color = buttonColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = Color.Lerp(buttonColor, isCloseButton ? Color.black : Color.white, 0.14f);
        colors.pressedColor = Color.Lerp(buttonColor, isCloseButton ? Color.black : Color.white, 0.22f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.40f, 0.40f, 0.40f, 0.54f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = labelObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.color = textColor;
        text.fontSize = 22f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        Shadow shadow = buttonObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.24f);
        shadow.effectDistance = new Vector2(0f, -2f);

        return button;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string value, float fontSize, FontStyles fontStyle)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(240f, 60f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.color = Color.white;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.enableWordWrapping = true;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.fontSizeMax = fontSize;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.text = label;
        }
    }

    private void UpdateTranscriptToggleLabel()
    {
        SetButtonLabel(toggleButton, isListening ? "Stop" : "Transcript");
    }

    private void OnTranscriptTogglePressed()
    {
        if (modalRoot == null) return;

        if (!isListening)
        {
            modalRoot.SetActive(true);
            StartListening();
        }
        else
        {
            StopListening();
            modalRoot.SetActive(false);
        }

        uiDirty = true;
        RefreshTranscriptText();
    }

    private void HideModal()
    {
        if (modalRoot != null) modalRoot.SetActive(false);
    }

    private void AddCurrentTranscriptToNote()
    {
        CommitPendingSentence();

        string transcript = BuildTranscriptText(false);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            SetStatus("No transcript to add");
            return;
        }

        EnsureNotesService();
        notesService.AppendTranscript(transcript);
        SetStatus("Added transcript to notes");
        Debug.Log("[SpeechTranscript] Note saved to " + notesService.NotesPath);
    }

    public void AddSelectedTranslationToNote(string selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            SetStatus("No translation selected");
            return;
        }

        EnsureNotesService();
        notesService.AppendSection("Slide translation", selectedText);
        SetStatus("Added selected translation to notes");
        Debug.Log("[SpeechTranscript] Selected translation saved to " + notesService.NotesPath);
    }

    private void EnsureNotesService()
    {
        if (notesService == null)
        {
            notesService = new LectureNotesService(notesFileName);
        }
    }

    private void ToggleNotesView()
    {
        showingNotes = !showingNotes;
        showingSummary = false;
        SetButtonLabel(viewNotesButton, showingNotes ? "Live" : "Notes");
        uiDirty = true;
    }

    private void ExportNotes()
    {
        string exportPath = notesService.ExportCopy();
        SetStatus("Exported to " + exportPath);
        Debug.Log("[SpeechTranscript] Notes exported to " + exportPath);
    }

    private void DeleteNotes()
    {
        notesService.Delete();
        SetStatus("Deleted notes");
        if (showingNotes)
        {
            uiDirty = true;
        }
    }

    private void OnSummarizePressed()
    {
        _ = SummarizeAsync();
    }

    private async Task SummarizeAsync()
    {
        CommitPendingSentence();

        string notes = notesService.ReadAll();
        string textToSummarize = string.IsNullOrWhiteSpace(notes) ? BuildTranscriptText(false) : notes;
        if (string.IsNullOrWhiteSpace(textToSummarize))
        {
            SetStatus("No transcript or notes to summarize");
            return;
        }

        if (httpPipelineClient == null)
        {
            httpPipelineClient = FindAnyObjectByType<HttpPipelineClient>();
        }

        if (httpPipelineClient == null)
        {
            httpPipelineClient = gameObject.AddComponent<HttpPipelineClient>();
        }

        SetStatus("Summarizing with Gemini");
        try
        {
            SpeechSummaryResponse response = await httpPipelineClient.SendSpeechSummaryAsync(
                textToSummarize,
                targetLanguage,
                speechBackendMockMode,
                llmProvider
            );
            summaryText = response != null ? response.summary_text : string.Empty;
            showingNotes = false;
            showingSummary = true;
            SetButtonLabel(viewNotesButton, "Notes");
            SetStatus("AI summary ready");
        }
        catch (Exception ex)
        {
            summaryText = "[summary error] " + ex.Message;
            showingNotes = false;
            showingSummary = true;
            SetStatus("AI summary failed");
        }

        uiDirty = true;
    }

    private void RefreshTranscriptText()
    {
        if (transcriptText == null) return;

        bool shouldStickToBottom = transcriptScroll == null ||
                                   transcriptScroll.verticalNormalizedPosition <= 0.08f ||
                                   string.IsNullOrWhiteSpace(transcriptText.text);

        transcriptText.text = showingSummary
            ? BuildSummaryText()
            : (showingNotes ? BuildNotesText() : BuildTranscriptText(true));
        uiDirty = false;

        if (shouldStickToBottom && transcriptScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            transcriptScroll.verticalNormalizedPosition = 0f;
        }
    }

    private string BuildNotesText()
    {
        string notes = notesService.ReadAll();
        return string.IsNullOrWhiteSpace(notes) ? "No saved notes." : notes.TrimEnd();
    }

    private string BuildSummaryText()
    {
        return string.IsNullOrWhiteSpace(summaryText) ? "No summary yet." : StripMarkdown(summaryText).TrimEnd();
    }

    /// <summary>
    /// Loại bỏ markdown formatting từ text LLM trả về
    /// </summary>
    private static string StripMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", m =>
        {
            string inner = m.Value;
            if (inner.Length > 6) inner = inner.Substring(3, inner.Length - 6);
            int nl = inner.IndexOf('\n');
            if (nl >= 0 && nl < 20) inner = inner.Substring(nl + 1);
            return inner.Trim();
        });
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\*\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^-\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
        return text;
    }

    private string BuildTranscriptText(bool includeTimestamps)
    {
        var builder = new StringBuilder();

        foreach (TranscriptEntry entry in transcriptEntries)
        {
            if (includeTimestamps)
            {
                builder.Append('[');
                builder.Append(entry.Timestamp.ToString("HH:mm:ss"));
                builder.Append("] ");
            }

            if (!string.IsNullOrWhiteSpace(entry.TranslatedText) || entry.TranslationPending)
            {
                builder.AppendLine("EN: " + entry.Text);
                builder.Append("VI: ");
                builder.AppendLine(entry.TranslationPending ? "Translating..." : entry.TranslatedText);
            }
            else
            {
                builder.AppendLine(entry.Text);
            }
        }

        if (!string.IsNullOrWhiteSpace(pendingSentenceText))
        {
            if (includeTimestamps)
            {
                builder.Append("[sentence] ");
            }

            builder.AppendLine(pendingSentenceText);
        }

        if (!string.IsNullOrWhiteSpace(interimText))
        {
            if (includeTimestamps)
            {
                builder.Append("[live] ");
            }

            builder.AppendLine(interimText);
        }

        if (builder.Length == 0 && includeTimestamps)
        {
            builder.AppendLine(isListening ? "Listening..." : "Transcript is paused.");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool EndsWithSentenceTerminator(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string trimmed = value.TrimEnd();
        char last = trimmed[trimmed.Length - 1];
        return last == '.' || last == '?' || last == '!' || last == ';' || last == ':';
    }

    private static string JoinSentenceFragments(string first, string second)
    {
        string left = NormalizeWhitespace(first);
        string right = NormalizeWhitespace(second);

        if (string.IsNullOrWhiteSpace(left)) return right;
        if (string.IsNullOrWhiteSpace(right)) return left;

        if (right.StartsWith(left, StringComparison.OrdinalIgnoreCase))
        {
            return right;
        }

        if (left.EndsWith(right, StringComparison.OrdinalIgnoreCase))
        {
            return left;
        }

        return left + " " + right;
    }

    private static float CalculateRms(float[] samples)
    {
        if (samples == null || samples.Length == 0) return 0f;

        double sum = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            float value = Mathf.Clamp(samples[i], -1f, 1f);
            sum += value * value;
        }

        return Mathf.Sqrt((float)(sum / samples.Length));
    }

    private static void AppendSamples(List<float> target, float[] samples)
    {
        if (target == null || samples == null || samples.Length == 0) return;

        for (int i = 0; i < samples.Length; i++)
        {
            target.Add(samples[i]);
        }
    }

    private static float[] ReadMicrophoneSamples(AudioClip clip, ref int readPosition, int currentPosition)
    {
        if (clip == null || currentPosition < 0) return new float[0];
        if (currentPosition == readPosition) return new float[0];

        int channels = Mathf.Max(1, clip.channels);
        int totalSamples = clip.samples;
        if (currentPosition > readPosition)
        {
            int frameCount = currentPosition - readPosition;
            float[] data = new float[frameCount * channels];
            clip.GetData(data, readPosition);
            readPosition = currentPosition;
            return data;
        }

        int tailFrames = totalSamples - readPosition;
        int headFrames = currentPosition;
        float[] combined = new float[(tailFrames + headFrames) * channels];

        if (tailFrames > 0)
        {
            float[] tail = new float[tailFrames * channels];
            clip.GetData(tail, readPosition);
            Array.Copy(tail, 0, combined, 0, tail.Length);
        }

        if (headFrames > 0)
        {
            float[] head = new float[headFrames * channels];
            clip.GetData(head, 0);
            Array.Copy(head, 0, combined, tailFrames * channels, head.Length);
        }

        readPosition = currentPosition;
        return combined;
    }

    private static byte[] EncodePcm16Bytes(float[] samples, int channels)
    {
        if (samples == null || samples.Length == 0) return new byte[0];

        int safeChannels = Mathf.Max(1, channels);
        int frameCount = samples.Length / safeChannels;
        byte[] bytes = new byte[frameCount * 2];

        for (int frame = 0; frame < frameCount; frame++)
        {
            float mixed = 0f;
            for (int channel = 0; channel < safeChannels; channel++)
            {
                mixed += samples[frame * safeChannels + channel];
            }

            mixed = Mathf.Clamp(mixed / safeChannels, -1f, 1f);
            short value = (short)Mathf.RoundToInt(mixed * short.MaxValue);
            int byteIndex = frame * 2;
            bytes[byteIndex] = (byte)(value & 0xff);
            bytes[byteIndex + 1] = (byte)((value >> 8) & 0xff);
        }

        return bytes;
    }

    private static string EncodePcm16Base64(float[] samples, int channels)
    {
        byte[] bytes = EncodePcm16Bytes(samples, channels);
        return bytes.Length == 0 ? string.Empty : Convert.ToBase64String(bytes);
    }

    private void SetStatus(string status)
    {
        if (statusText != null) statusText.text = status;
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string[] parts = value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string ResolveMicrophoneDeviceName()
    {
        string[] devices = Microphone.devices;
        return devices != null && devices.Length > 0 ? devices[0] : null;
    }

    private int ResolveMicrophoneSampleRate(string deviceName)
    {
        int requested = Mathf.Max(1, microphoneSampleRateHz);
        if (string.IsNullOrEmpty(deviceName))
        {
            return requested;
        }

        Microphone.GetDeviceCaps(deviceName, out int minFrequency, out int maxFrequency);
        if (minFrequency == 0 && maxFrequency == 0)
        {
            return requested;
        }

        if (maxFrequency > 0 && requested > maxFrequency)
        {
            return maxFrequency;
        }

        if (minFrequency > 0 && requested < minFrequency)
        {
            return minFrequency;
        }

        return requested;
    }
}
