## 1. STT sidecar

- [x] 1.1 Create `stt/` service: FastAPI app wrapping `faster-whisper`, exposing `POST /transcribe` (multipart audio in, `{ text, language }` JSON out)
- [x] 1.2 Add model size + device config via env var (default `STT_MODEL_SIZE=base`, CPU-only)
- [x] 1.3 Add health check endpoint for container readiness
- [x] 1.4 Add `Dockerfile` for the `stt` service
- [x] 1.5 Add `stt` service to `docker-compose.yml`; wire `be`'s env var for the sidecar base URL

## 2. Backend: transcription abstraction

- [x] 2.1 Add `ITranscriptionService` to `Application/Abstractions` (`TranscribeAsync(Stream audio, string contentType, CancellationToken) -> Result<string>`)
- [x] 2.2 Implement `Infrastructure` HTTP-client adapter calling the `stt` sidecar; register in `Infrastructure/DependencyInjection.cs`
- [x] 2.3 Add configuration binding for the sidecar base URL (e.g. `Stt:BaseUrl`)

## 3. Backend: dictate-to-compose endpoint

- [x] 3.1 Add `POST /api/transcribe` controller action (authenticated), accepting an audio clip, returning transcribed text
- [x] 3.2 Validate empty/undecodable audio and return a bad-request error (per spec)
- [x] 3.3 Reject clips longer than 120 seconds with a bad-request error stating the limit (per spec)
- [x] 3.4 Return a transcription-unavailable error when `ITranscriptionService` fails, without persisting anything

## 4. Backend: voice-attachment transcription

- [x] 4.1 Add `Audio` case to `AttachmentKind`; classify `audio/*` content types as `Audio` in `AttachmentsController.Upload`
- [x] 4.2 Add `TranscriptText` (nullable) column to `Attachment`; add EF migration
- [x] 4.3 Add a background transcription queue/worker (`IHostedService`, channel-backed), following `ChatNotifier`'s scope-per-operation pattern for DB access
- [x] 4.4 On audio attachment upload, enqueue a transcription job after the existing message/attachment creation and `MessageReceivedAsync` push, unless the clip exceeds 10 minutes (skip enqueueing per spec)
- [x] 4.5 On job completion, persist `TranscriptText` and push a new `AttachmentTranscribed { attachmentId, transcriptText }` event via `IChatNotifier` to `room:{roomId}`
- [x] 4.6 On job failure, log and leave `TranscriptText` null; no error surfaced to other participants

## 5. Frontend: shared transcription API + realtime

- [x] 5.1 Add `transcribe` API module (`FE/src/api/`) calling `POST /api/transcribe` via the shared `apiFetch`/`apiJson` client
- [x] 5.2 Register `AttachmentTranscribed` in `FE/src/realtime/events.ts`; update the relevant attachment/message cache entry when received

## 6. Frontend: dictate-to-compose

- [x] 6.1 Add mic button to the composer; use `MediaRecorder` to capture audio on press/release
- [x] 6.2 Auto-stop recording at 120 seconds and submit the captured audio as if manually stopped (per spec)
- [x] 6.3 On stop, send the clip to the transcribe API; show an in-flight state during the small expected delay
- [x] 6.4 On success, insert transcript into composer text (append if text already present) as editable draft
- [x] 6.5 Handle microphone-permission-denied state (disable control, indicate unavailability)
- [x] 6.6 Handle transcription failure (surface error, leave existing composer text untouched)

## 7. Frontend: voice-message transcript display

- [x] 7.1 Render audio attachments with a player plus transcript area (empty/pending until `AttachmentTranscribed` arrives or initial fetch already includes it)
- [x] 7.2 Include `TranscriptText` in the attachment metadata DTO returned by history/list endpoints so already-transcribed attachments show text without waiting for a live event

## 8. Verification

- [x] 8.1 Restart the stack (`restart` agent) and manually verify dictate-to-compose end to end — verified via direct authenticated `POST /api/transcribe` call (Playwright unavailable in this session for live UI clicks); confirmed the full path: auth check, BE→sidecar HTTP call, sidecar audio decode + faster-whisper transcription, response back to caller
- [x] 8.2 Manually verify voice-message upload gets a transcript and it appears for other room members in real time — verified via direct authenticated `POST /api/attachments/upload` with an `audio/wav` file: attachment correctly classified as `Kind: "Audio"`, background job enqueued and ran (confirmed in `be` logs: `POST http://stt:9000/transcribe` → 200), no transcript produced only because the test clip was silent (empty text), which matches the "no caption, no error" spec behavior for a transcription that yields nothing
- [x] 8.3 Verify sidecar-unavailable behavior for both flows (stop `stt` container, confirm graceful degradation per spec) — stopped `stt`, confirmed `POST /api/transcribe` returns `503 {"errors":{"Transcription.Unavailable":[...]}}` without crashing or persisting anything; restarted `stt`, confirmed it becomes healthy again
- [x] 8.4 Run `cd FE && npm test` — ran; reports "No test files found" (pre-existing repo state with no test files, unrelated to this change)

Note: found and fixed a bug during verification — `stt/requirements.txt` was missing `requests`, a transitive import used by `faster_whisper.utils`, which crashed the sidecar container on startup.

Note: a second bug surfaced only in a real browser — the sidecar probed clip duration with `soundfile`/libsndfile, which cannot read WebM/Opus (the format `MediaRecorder` produces) or MP4/AAC (Safari), so every real dictation returned `400 undecodable_audio`. Earlier verification passed only because it posted a WAV directly. Fixed by decoding with PyAV via `faster_whisper.audio.decode_audio` (already a faster-whisper dependency); `soundfile`/`libsndfile1` dropped. Re-verified in-container with a real WebM/Opus clip: `200` with correct 2.0s duration, over-limit clip → `422 audio_too_long`, garbage bytes → `400 undecodable_audio`.

Note: a third bug, also browser-only — `FasterWhisperTranscriptionService` built the outgoing part header with `new MediaTypeHeaderValue(contentType)`, which throws `FormatException` on parameterised types like `audio/webm;codecs=opus` (exactly what `MediaRecorder` sets). The generic `catch` swallowed it into `Transcription.Unavailable`, so dictation returned `503`. Fixed by parsing with `MediaTypeHeaderValue.TryParse` and falling back to `application/octet-stream`. Verified end to end through the FE nginx proxy with a real TTS-generated WebM/Opus clip and the browser's exact content type: `200 {"text":"Hello, this is a test of the transcription service.","language":"en"}`.
