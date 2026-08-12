# speech-to-text Specification

## Purpose

Local, self-hosted transcription of short audio clips into editable text, so users can dictate messages and read voice notes without sending audio to a third-party service.

## Requirements

### Requirement: Transcribing an audio clip
An authenticated user SHALL be able to submit a single recorded audio clip and receive back the transcribed text. Transcription SHALL run against locally hosted infrastructure only — no audio or derived text SHALL be sent to a third-party cloud transcription service. The request SHALL be a single non-streaming exchange: the full clip is sent once recording stops, and the caller waits for one response.

#### Scenario: Successful transcription
- **WHEN** an authenticated user submits a recorded audio clip for transcription
- **THEN** the system returns the transcribed text for that clip

#### Scenario: Unauthenticated request
- **WHEN** a request to transcribe audio is made without a valid session
- **THEN** the request is refused as unauthorized

#### Scenario: Empty or unreadable audio
- **WHEN** the submitted clip is empty or not a decodable audio format
- **THEN** the request is rejected as a bad request, and no partial transcript is returned

#### Scenario: Transcription service unavailable
- **WHEN** the local transcription backend cannot be reached or fails to process the clip
- **THEN** the request fails with an error indicating transcription is unavailable, and the caller's prior input (composer text, in-flight attachment) is left unchanged

### Requirement: Dictate into the composer
The message composer SHALL offer a control that records audio from the user's microphone, sends it for transcription once recording stops, and inserts the resulting text into the composer as editable draft text. The transcript SHALL NOT be sent as a message automatically; the user reviews and sends it like any other typed text.

#### Scenario: Dictating a message
- **WHEN** a user starts the dictate control, speaks, and stops recording
- **THEN** the transcribed text appears in the composer input, editable, and is not sent until the user sends it explicitly

#### Scenario: Dictation with existing composer text
- **WHEN** the composer already contains text at the moment dictation completes
- **THEN** the transcribed text is appended to the existing text rather than replacing it

#### Scenario: Recording permission denied
- **WHEN** the user denies microphone access
- **THEN** the dictate control shows that microphone access is unavailable and no transcription request is made

### Requirement: Dictation length limit
The dictate control SHALL limit a single recording to 120 seconds, automatically stopping the recording and submitting it for transcription once reached. The `POST /api/transcribe` endpoint SHALL also reject a submitted clip exceeding 120 seconds, independent of the client-side stop, so the limit holds even if the client is bypassed.

#### Scenario: Recording reaches the limit
- **WHEN** a user keeps the dictate control active for 120 seconds
- **THEN** recording stops automatically and the captured audio is submitted for transcription as if the user had stopped it manually

#### Scenario: Oversized clip submitted directly
- **WHEN** a transcription request carries a clip longer than 120 seconds
- **THEN** the request is rejected as a bad request stating the limit, and no transcription is attempted
