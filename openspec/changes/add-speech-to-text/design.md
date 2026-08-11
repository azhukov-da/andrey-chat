## Context

See [proposal.md](proposal.md) for motivation. Relevant existing constraints:

- `AttachmentsController.Upload` ([BE/Web/Controllers/AttachmentsController.cs](../../../BE/Web/Controllers/AttachmentsController.cs)) already creates a `Message` + `Attachment` synchronously and pushes it via `IChatNotifier.MessageReceivedAsync`. Audio files already flow through this endpoint today as generic `File`-kind attachments (`AttachmentKind` has no `Audio` case yet).
- `ChatNotifier` is a singleton that resolves scoped services (`IApplicationDbContext`) through an injected `IServiceProvider` + `CreateScope()` — any new background work that touches the DB and then notifies must follow this same pattern.
- Services return `Result<T>`; controllers/hub translate failures uniformly. `Application/Abstractions` interfaces are implemented in `Infrastructure` and registered in `Application/DependencyInjection.cs` / `Infrastructure/DependencyInjection.cs`.
- No GPU in the deployment target; `docker-compose.yml` currently runs `db`, `be`, `fe`.

## Goals / Non-Goals

**Goals:**
- One transcription pipeline, reused by both entry points (dictate-to-compose, voice-attachment captioning).
- Fully local: no audio or transcript leaves the user's infrastructure.
- `ITranscriptionService` isolates BE/FE from the sidecar's implementation so the engine can change later without touching `Web` or `FE`.
- Voice-attachment upload stays synchronous and fast; transcription happens after, without blocking the upload response (per the `attachments` delta spec: "message created immediately... transcript added once available").

**Non-Goals:**
- Streaming/live partial transcripts (explicitly out of scope — see proposal).
- Speaker diarization, punctuation-perfect formatting, or translation.
- Persisting dictate-to-compose audio recordings (only the resulting text matters there).

## Decisions

### Shared interface, two callers
`ITranscriptionService.TranscribeAsync(Stream audio, string contentType, CancellationToken)` returns `Result<string>` (transcript text). One HTTP-based `Infrastructure` implementation (`FasterWhisperTranscriptionService` or similar) calls the sidecar. Both the new `POST /api/transcribe` endpoint (dictate) and the attachment-upload background step (voice messages) call the same interface — neither knows the sidecar exists.

**Alternatives considered:** separate services per use case — rejected, since both reduce to "audio in, text out" and duplicating the interface would duplicate the swap-later cost the proposal explicitly wants to avoid.

### Sidecar: faster-whisper behind FastAPI
New `stt` service in `docker-compose.yml`: a small FastAPI app wrapping `faster-whisper` (CTranslate2 runtime), CPU-only, exposing `POST /transcribe` (multipart audio → `{ text, language }` JSON). Chosen over whisper.cpp-server (slower on CPU) and Whisper.net (same whisper.cpp-grade speed, would have stayed all-.NET) — see proposal for the full comparison. This is the first Python component in the repo, scoped entirely to this one container.

### Model size: `base`, configurable
Default to the Whisper `base` model (multilingual, quantized int8 via CTranslate2) as the CPU/latency-vs-accuracy default. Exposed as an env var (`STT_MODEL_SIZE`) on the sidecar so it can be tuned per-deployment without a code change. Not spec-visible — an operational knob, not a behavior contract.

### Language: auto-detect, no language picker
faster-whisper auto-detects language per clip; no FE language selector is added. Keeps both entry points single-control (mic button only). If a specific deployment needs to pin a language for accuracy, that's an `stt` sidecar config change, not a spec change.

### Dictate audio is never persisted
The `POST /api/transcribe` endpoint reads the uploaded clip into memory/stream, forwards it to the sidecar, returns the text, and discards the audio — no `Attachment` row, no file-storage write. This differs from voice-message attachments, which are already persisted via the existing `attachments` upload path regardless of this change. Minimizes what's retained for a flow that's explicitly "text lands in the composer, audio was just a means to that."

### Voice-attachment transcription runs after upload, asynchronously
`AttachmentKind` gains an `Audio` case (content type `audio/*`). On upload, `AttachmentsController.Upload` behaves exactly as today (message + attachment created, `MessageReceivedAsync` fired) and, for audio attachments, additionally enqueues a background transcription job (channel-backed `IHostedService`, consistent with `ChatNotifier`'s own scope-per-operation pattern). On completion, the job updates the `Attachment`'s new `TranscriptText` column and pushes a new SignalR event, `AttachmentTranscribed { attachmentId, transcriptText }`, to the `room:{roomId}` group — mirroring how `MessageEdited` updates an already-delivered message in place. On failure, the job logs and leaves `TranscriptText` null; no error reaches other participants (per spec).

**Alternatives considered:** transcribing inline before the upload responds — rejected, it would make every voice-message upload wait on CPU-bound transcription, contradicting "message created immediately."

### Duration caps: 120s (dictate), 10min (attachment transcription eligibility)
The `stt` sidecar is a single CPU-bound service shared by both flows; an unbounded clip lets one request monopolize it and delay everything queued behind it. Dictate-to-compose is synchronous and user-facing, so its cap is tight (120s, enforced client-side via auto-stop and server-side on `POST /api/transcribe` so it holds even if the client is bypassed). Voice-attachment uploads are governed by the existing `attachments` size limits and are never blocked by this cap — a clip over 10 minutes still uploads and plays normally, it's just skipped for transcription (treated identically to a transcription failure, per spec) rather than tying up the shared sidecar on a job nobody is waiting on synchronously.

**Alternatives considered:** a byte-size cap instead of duration — rejected, since transcription cost scales with duration, not bytes, and the existing 20 MB attachment cap is a loose, bitrate-dependent proxy for that; a duration cap on transcription eligibility is a closer proxy for the resource it's protecting.

## Risks / Trade-offs

- **[Risk] CPU-only faster-whisper latency under load** (multiple concurrent transcriptions on modest hardware) → Mitigation: background queue for attachments already decouples this from the upload path; dictate-to-compose accepts "small delay" per the agreed requirement; duration caps on both flows bound the worst case a single clip can cost. If it becomes a bottleneck, the queue can be bounded/rate-limited further without a spec change.
- **[Risk] First Python dependency in an otherwise .NET/TS repo** (new base image, new dependency ecosystem to patch/update) → Mitigation: contained entirely to the `stt` container; `ITranscriptionService` means this can be swapped for a .NET-native implementation later without touching `Web`/`FE`.
- **[Risk] Sidecar unavailable** (container down, crashed, cold-starting) → Mitigation: `ITranscriptionService` returns `Result.Failure`; dictate returns a user-visible transcription-unavailable error without losing composer state (per spec), attachment upload still succeeds and simply has no transcript.

## Migration Plan

- New `Attachment.TranscriptText` (nullable) column via EF migration; existing attachments remain `NULL` (no backfill — historical audio attachments simply have no transcript unless re-uploaded).
- New `stt` service added to `docker-compose.yml`; `be` gains its base URL via an env var (e.g. `Stt__BaseUrl`), same pattern as other configuration in `appsettings.json`/env vars.
- No breaking changes to existing endpoints or events; `AttachmentTranscribed` and `POST /api/transcribe` are additive.
