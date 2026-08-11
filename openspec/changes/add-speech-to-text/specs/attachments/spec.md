## ADDED Requirements

### Requirement: Voice attachment transcription
An attachment whose content type identifies it as audio SHALL be automatically submitted for local transcription after upload, and the resulting transcript SHALL be attached to the attachment's metadata as a caption, distinct from the uploader's optional comment. The transcript SHALL appear once available without requiring the viewer to play the audio.

#### Scenario: Voice attachment gets a transcript
- **WHEN** a member uploads an audio file to a chat
- **THEN** the message is created immediately as with any attachment, and a transcript caption is added to the attachment once transcription completes

#### Scenario: Transcription fails for a voice attachment
- **WHEN** transcription of an uploaded audio attachment fails or the local transcription backend is unavailable
- **THEN** the attachment remains available for playback and download without a transcript, and no error is shown to other participants

#### Scenario: Transcript shown alongside playback
- **WHEN** a voice attachment with a completed transcript is viewed
- **THEN** the transcript text is displayed together with the audio player

### Requirement: Transcription eligibility length limit
An audio attachment longer than 10 minutes SHALL still upload and be playable/downloadable per the existing attachment rules, but SHALL NOT be submitted for transcription; it is treated the same as a transcription failure (no caption, no error shown to other participants).

#### Scenario: Long voice attachment uploads without a transcript
- **WHEN** a member uploads an audio file longer than 10 minutes
- **THEN** the attachment is stored and playable as normal, and no transcription job is enqueued for it
