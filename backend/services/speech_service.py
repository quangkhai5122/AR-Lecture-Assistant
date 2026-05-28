from __future__ import annotations

import base64
import json
import os
import queue
import threading
from dataclasses import dataclass
from typing import Any

import requests

from services.errors import PipelineError


@dataclass
class SpeechToTextResult:
    transcript: str
    provider: str
    mock_used: bool
    confidence: float | None = None
    warnings: list[str] | None = None


class SpeechToTextService:
    """Speech-to-text adapter for Google Cloud Speech-to-Text."""

    SUPPORTED_PROVIDERS = {"mock", "google"}

    MOCK_TRANSCRIPTS = [
        "Today we will review the main idea from the previous slide.",
        "This definition is important because it appears in later examples.",
        "Notice how the input changes before the model returns a prediction.",
    ]

    def __init__(self):
        self._mock_index = 0

    def recognize(
        self,
        audio_base64: str,
        audio_encoding: str = "LINEAR16",
        sample_rate_hz: int = 16000,
        language_code: str = "en-US",
        force_mock: bool = False,
        provider: str | None = None,
    ) -> SpeechToTextResult:
        resolved_provider = self._resolve_provider(force_mock, provider)
        if resolved_provider == "mock":
            transcript = self.MOCK_TRANSCRIPTS[self._mock_index % len(self.MOCK_TRANSCRIPTS)]
            self._mock_index += 1
            return SpeechToTextResult(
                transcript=transcript,
                provider="mock",
                mock_used=True,
                confidence=1.0,
                warnings=[],
            )

        if not audio_base64.strip():
            raise PipelineError("Field 'audio_base64' is required when speech mock=false.")

        if os.getenv("GOOGLE_SPEECH_API_KEY") or os.getenv("GOOGLE_CLOUD_SPEECH_API_KEY"):
            return self._recognize_google_rest(
                audio_base64=audio_base64,
                audio_encoding=audio_encoding,
                sample_rate_hz=sample_rate_hz,
                language_code=language_code,
            )

        return self._recognize_google_client(
            audio_base64=audio_base64,
            audio_encoding=audio_encoding,
            sample_rate_hz=sample_rate_hz,
            language_code=language_code,
        )

    def _resolve_provider(self, force_mock: bool, provider: str | None) -> str:
        if force_mock:
            return "mock"

        selected = (provider or os.getenv("SPEECH_PROVIDER") or "google").strip().lower()
        if selected not in self.SUPPORTED_PROVIDERS:
            raise PipelineError(
                "Unsupported speech provider "
                f"'{selected}'. Expected one of: {sorted(self.SUPPORTED_PROVIDERS)}."
            )
        return selected

    def _recognize_google_rest(
        self,
        audio_base64: str,
        audio_encoding: str,
        sample_rate_hz: int,
        language_code: str,
    ) -> SpeechToTextResult:
        api_key = os.getenv("GOOGLE_SPEECH_API_KEY") or os.getenv("GOOGLE_CLOUD_SPEECH_API_KEY")
        url = os.getenv("GOOGLE_SPEECH_RECOGNIZE_URL", "https://speech.googleapis.com/v1/speech:recognize")
        payload = {
            "config": {
                "encoding": audio_encoding,
                "sampleRateHertz": sample_rate_hz,
                "languageCode": language_code,
                "enableAutomaticPunctuation": True,
            },
            "audio": {
                "content": audio_base64,
            },
        }

        timeout = float(os.getenv("GOOGLE_SPEECH_TIMEOUT_SECONDS", "30"))
        try:
            response = requests.post(url, params={"key": api_key}, json=payload, timeout=timeout)
            response.raise_for_status()
            data = response.json()
        except Exception as exc:
            raise PipelineError(
                f"Google Speech-to-Text request failed: {exc}",
                status_code=502,
                code="speech_provider_failed",
            ) from exc

        return self._parse_google_response(data, provider="google")

    def _recognize_google_client(
        self,
        audio_base64: str,
        audio_encoding: str,
        sample_rate_hz: int,
        language_code: str,
    ) -> SpeechToTextResult:
        try:
            from google.cloud import speech
        except Exception as exc:
            raise PipelineError(
                "SPEECH_PROVIDER=google requires either GOOGLE_SPEECH_API_KEY "
                "or google-cloud-speech with GOOGLE_APPLICATION_CREDENTIALS.",
                code="speech_provider_not_configured",
            ) from exc

        try:
            audio_bytes = base64.b64decode(audio_base64, validate=True)
        except Exception as exc:
            raise PipelineError("Field 'audio_base64' must be valid base64 audio.") from exc

        encoding_name = audio_encoding.strip().upper()
        if not hasattr(speech.RecognitionConfig.AudioEncoding, encoding_name):
            raise PipelineError(f"Unsupported Google speech audio encoding: {audio_encoding}")

        timeout = float(os.getenv("GOOGLE_SPEECH_TIMEOUT_SECONDS", "30"))
        try:
            client = speech.SpeechClient()
            config = speech.RecognitionConfig(
                encoding=getattr(speech.RecognitionConfig.AudioEncoding, encoding_name),
                sample_rate_hertz=sample_rate_hz,
                language_code=language_code,
                enable_automatic_punctuation=True,
            )
            audio = speech.RecognitionAudio(content=audio_bytes)
            response = client.recognize(config=config, audio=audio, timeout=timeout)
        except Exception as exc:
            raise PipelineError(
                f"Google Speech-to-Text client request failed: {exc}",
                status_code=502,
                code="speech_provider_failed",
            ) from exc

        transcript_parts: list[str] = []
        confidences: list[float] = []
        for result in response.results:
            if not result.alternatives:
                continue
            alternative = result.alternatives[0]
            transcript_parts.append(alternative.transcript)
            if alternative.confidence:
                confidences.append(float(alternative.confidence))

        transcript = " ".join(part.strip() for part in transcript_parts if part and part.strip()).strip()
        confidence = sum(confidences) / len(confidences) if confidences else None
        warnings = [] if transcript else ["Google Speech-to-Text returned no transcript."]
        return SpeechToTextResult(
            transcript=transcript,
            provider="google",
            mock_used=False,
            confidence=confidence,
            warnings=warnings,
        )

    def _parse_google_response(self, data: dict[str, Any], provider: str) -> SpeechToTextResult:
        if not isinstance(data, dict):
            raise PipelineError(
                f"Unexpected Google Speech-to-Text response shape: {data}",
                status_code=502,
                code="speech_provider_bad_response",
            )

        transcript_parts: list[str] = []
        confidences: list[float] = []
        results = data.get("results", [])
        if isinstance(results, list):
            for result in results:
                if not isinstance(result, dict):
                    continue
                alternatives = result.get("alternatives", [])
                if not alternatives or not isinstance(alternatives, list):
                    continue
                best = alternatives[0]
                if not isinstance(best, dict):
                    continue
                transcript = best.get("transcript")
                if isinstance(transcript, str) and transcript.strip():
                    transcript_parts.append(transcript.strip())
                confidence = best.get("confidence")
                if isinstance(confidence, (int, float)):
                    confidences.append(float(confidence))

        transcript = " ".join(transcript_parts).strip()
        confidence = sum(confidences) / len(confidences) if confidences else None
        warnings = [] if transcript else ["Google Speech-to-Text returned no transcript."]
        return SpeechToTextResult(
            transcript=transcript,
            provider=provider,
            mock_used=False,
            confidence=confidence,
            warnings=warnings,
        )

    def stream_websocket(self, ws) -> None:
        config_message = ws.receive()
        if not isinstance(config_message, str):
            ws.send(json.dumps({
                "type": "error",
                "error": "First WebSocket message must be a JSON config object.",
            }))
            return

        try:
            config = json.loads(config_message)
        except json.JSONDecodeError as exc:
            ws.send(json.dumps({
                "type": "error",
                "error": f"Invalid stream config JSON: {exc}",
            }))
            return

        if bool(config.get("mock", False)):
            self._stream_mock_websocket(ws)
            return

        provider = self._resolve_provider(False, config.get("speech_provider"))
        if provider != "google":
            ws.send(json.dumps({
                "type": "error",
                "error": f"Streaming speech provider '{provider}' is not supported.",
            }))
            return

        self._stream_google_websocket(ws, config)

    def _stream_mock_websocket(self, ws) -> None:
        for transcript in self.MOCK_TRANSCRIPTS:
            ws.send(json.dumps({
                "type": "result",
                "transcript": transcript,
                "is_final": True,
                "confidence": 1.0,
                "provider": "mock",
            }))
        ws.send(json.dumps({"type": "done"}))

    def _stream_google_websocket(self, ws, config: dict[str, Any]) -> None:
        try:
            from google.cloud import speech
        except Exception as exc:
            ws.send(json.dumps({
                "type": "error",
                "error": (
                    "Streaming Google Speech-to-Text requires google-cloud-speech "
                    "and GOOGLE_APPLICATION_CREDENTIALS."
                ),
            }))
            return

        audio_queue: queue.Queue[bytes | None] = queue.Queue(maxsize=32)
        closed = threading.Event()

        def receive_audio() -> None:
            try:
                while not closed.is_set():
                    message = ws.receive()
                    if message is None:
                        break

                    if isinstance(message, str):
                        try:
                            data = json.loads(message)
                        except json.JSONDecodeError:
                            continue
                        if data.get("type") == "stop":
                            break
                        continue

                    if isinstance(message, bytes) and message:
                        audio_queue.put(message)
            finally:
                closed.set()
                audio_queue.put(None)

        receiver = threading.Thread(target=receive_audio, daemon=True)
        receiver.start()

        audio_encoding = str(config.get("audio_encoding", "LINEAR16")).strip().upper()
        language_code = str(config.get("language_code", "en-US")).strip() or "en-US"
        sample_rate_hz = int(config.get("sample_rate_hz", 16000))
        interim_results = bool(config.get("interim_results", True))

        if not hasattr(speech.RecognitionConfig.AudioEncoding, audio_encoding):
            closed.set()
            ws.send(json.dumps({
                "type": "error",
                "error": f"Unsupported Google speech audio encoding: {audio_encoding}",
            }))
            return

        recognition_config = speech.RecognitionConfig(
            encoding=getattr(speech.RecognitionConfig.AudioEncoding, audio_encoding),
            sample_rate_hertz=sample_rate_hz,
            language_code=language_code,
            enable_automatic_punctuation=True,
        )
        streaming_config = speech.StreamingRecognitionConfig(
            config=recognition_config,
            interim_results=interim_results,
            single_utterance=False,
        )

        def request_stream():
            yield speech.StreamingRecognizeRequest(streaming_config=streaming_config)
            while not closed.is_set():
                chunk = audio_queue.get()
                if chunk is None:
                    break
                yield speech.StreamingRecognizeRequest(audio_content=chunk)

        timeout = float(os.getenv("GOOGLE_SPEECH_STREAM_TIMEOUT_SECONDS", "305"))
        try:
            client = speech.SpeechClient()
            responses = client.streaming_recognize(requests=request_stream(), timeout=timeout)
            for response in responses:
                for result in response.results:
                    if not result.alternatives:
                        continue
                    alternative = result.alternatives[0]
                    transcript = (alternative.transcript or "").strip()
                    if not transcript:
                        continue
                    ws.send(json.dumps({
                        "type": "result",
                        "transcript": transcript,
                        "is_final": bool(result.is_final),
                        "stability": float(result.stability or 0.0),
                        "confidence": float(alternative.confidence or 0.0),
                        "provider": "google",
                    }))
        except Exception as exc:
            ws.send(json.dumps({
                "type": "error",
                "error": f"Google streaming Speech-to-Text failed: {exc}",
            }))
        finally:
            closed.set()
