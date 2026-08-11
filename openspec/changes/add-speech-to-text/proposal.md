## Why

Typing on mobile or while multitasking is friction users routinely want to avoid, and voice messages are currently opaque — a teammate has to play the audio to know what it says. A local (self-hosted, not cloud-API) speech-to-text pipeline removes both frictions without sending user audio to a third party.

## What Changes

- Add a new `speech-to-text` capability: a shared, non-streaming transcription pipeline used by two entry points.
  - **Dictate-to-compose**: a mic button next to the message composer records audio client-side, sends it once recording stops, and drops the returned transcript into the composer as editable text (not auto-sent).
  - **Voice-message transcription**: an audio attachment (recorded or uploaded) is automatically transcribed after upload, and the resulting caption is shown alongside the attachment.
- Add `POST /api/transcribe`: authenticated endpoint that accepts an audio clip and returns transcribed text.
- Add `ITranscriptionService` in `Application/Abstractions` (mirrors the existing `IFileStorage` pattern), implemented by an `Infrastructure` HTTP-client adapter that calls a new sidecar service.
- Add a new `stt` service to `docker-compose.yml`: a FastAPI wrapper around `faster-whisper`, CPU-only inference, exposing a multipart-audio-in / JSON-text-out endpoint. This is the first Python component in the stack — accepted for CPU throughput over the .NET-native alternative (Whisper.net), which runs at plain whisper.cpp speed.
- Voice-message attachments gain an auto-generated caption populated after transcription completes (attachment metadata, not the user-supplied comment).

## Capabilities

### New Capabilities
- `speech-to-text`: local, non-streaming audio-to-text transcription, exposed via `POST /api/transcribe` and consumed by the composer's dictate flow.

### Modified Capabilities
- `attachments`: audio attachments gain an auto-generated transcript caption, populated asynchronously after upload and displayed alongside the attachment.

## Impact

- **Backend**: new `Application/Abstractions/ITranscriptionService.cs`, new `Infrastructure` HTTP adapter, new `Web/Controllers/TranscriptionController.cs` (or equivalent), DI registration, new `Domain` field for attachment transcript caption + EF migration.
- **Frontend**: new mic-button control in the composer (`MediaRecorder` capture), transcript-display addition to the attachment renderer, new API module for `/api/transcribe`.
- **Infra**: new `stt` service in `docker-compose.yml` (Python/FastAPI/faster-whisper image), no GPU requirement, CPU-only model sized for acceptable latency.
- **Dependencies**: introduces Python into an otherwise .NET/TS repo, scoped entirely to the new sidecar container.
