# Speech Transcript Integration Plan

## Current integration

- Active Unity code path: `Assets/Scripts/...`.
- `SpeechTranscriptController` is attached automatically by `UIManager`.
- Unity opens WebSocket `/speech/stream`, sends PCM16 microphone frames continuously, and receives interim/final Google STT results.
- Backend `/speech/stream` uses Google Cloud Speech-to-Text `streaming_recognize` when `SPEECH_PROVIDER=google`.
- Backend `/speech/transcribe` remains as a non-streaming fallback/test endpoint.
- Backend `/speech/translate-text` uses Gemini when `LLM_PROVIDER=gemini`.
- The old Android platform `SpeechRecognizer` path remains as a fallback when backend speech translation is disabled.
- Default speech source language is `en-US`, matching the current English-to-Vietnamese lecture translation direction. Change `recognitionLanguage` to `vi-VN` if the lecturer speaks Vietnamese.
- Unity Editor and unsupported platforms use a mock speech feed for UI testing.
- Raw microphone audio is not persisted in phase 1. The OS recognizer consumes the microphone stream and the app stores the rolling text transcript.
- The transcript modal keeps only the most recent 10 seconds of finalized transcript plus the current partial phrase.
- Final speech fragments are grouped into sentence-level transcript entries. Gemini translation is called only after punctuation or after a configurable silence timeout, default 2.2 seconds.
- `Add to note` appends the current rolling transcript to `Application.persistentDataPath/lecture_notes.md`.
- The modal can view, export, and delete the local notes file.
- `AI summary` calls backend `/speech/summarize`, which uses Gemini.

## Modal refresh strategy

Do not reload or rebuild the modal continuously. Keep the modal GameObject alive and update only the transcript text field.

Recommended default:

- Refresh UI at 0.25 seconds while transcript changes.
- Append/prune data in memory immediately when speech results arrive.
- Prune displayed transcript by timestamp, not by line count.
- Auto-scroll only when the user is already near the bottom; if the user scrolls up, do not force-scroll.

This gives a live feel without rebuilding UI hierarchy every frame.

## Next phases

1. Let the user select language (`vi-VN`, `en-US`, etc.) from the UI.
2. Add a real notes viewer/export flow instead of only appending to a markdown file.
3. Enable `AI summary` by sending the last transcript window or selected notes to an LLM endpoint.
4. Optionally add backend/cloud STT if Android platform STT quality is not enough.
