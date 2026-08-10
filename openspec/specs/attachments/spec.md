# Attachments Specification

## Purpose

Sharing images and arbitrary files inside chats, with original file names, optional comments, size ceilings, and downloads restricted to current participants of the chat that holds them.

## Requirements

### Requirement: Uploading attachments
A member of a chat SHALL be able to upload an image or an arbitrary file. Each upload SHALL become a message in that chat authored by the uploader, so the file is part of the chat history and is pushed to other participants in real time. Uploads by non-members, banned users, or into a deleted room SHALL be refused, as SHALL empty files.

#### Scenario: Uploading a file
- **WHEN** a member picks a file with the attach control
- **THEN** the file is stored, a message carrying its metadata appears in the chat for all participants, and an upload indicator is shown while in flight

#### Scenario: Uploading several files at once
- **WHEN** the user selects multiple files
- **THEN** each file is uploaded and appears as its own message

#### Scenario: Non-member upload
- **WHEN** a user who is not a member uploads to a chat
- **THEN** the request is refused as not-a-member

#### Scenario: Banned uploader
- **WHEN** a user banned from the room uploads to it
- **THEN** the request is refused as banned

#### Scenario: Empty file
- **WHEN** the request carries no file or a zero-length file
- **THEN** the request is rejected as a bad request

### Requirement: Upload by paste
The composer SHALL accept attachments pasted from the clipboard as well as chosen through the attach control, using the same rules and limits.

#### Scenario: Pasting an image
- **WHEN** the user pastes clipboard content containing a file into the message input
- **THEN** the file is uploaded as an attachment instead of being inserted as text

### Requirement: Size limits by kind
An attachment whose content type identifies it as an image SHALL be limited to 3 MB; any other file SHALL be limited to 20 MB. Oversized uploads SHALL be rejected with a message naming the applicable limit, and the error SHALL be shown in the composer.

#### Scenario: Oversized image
- **WHEN** a user uploads an image larger than 3 MB
- **THEN** the upload is rejected with an image-too-large error stating the 3 MB limit

#### Scenario: Oversized file
- **WHEN** a user uploads a non-image file larger than 20 MB
- **THEN** the upload is rejected with a file-too-large error stating the 20 MB limit

### Requirement: Attachment metadata
The system SHALL preserve each attachment's original file name and content type, record its size and whether it is an image, and accept an optional comment supplied by the uploader. The comment SHALL also become the text of the message carrying the attachment.

#### Scenario: Original name preserved
- **WHEN** an attachment is listed or downloaded
- **THEN** it carries the file name as uploaded, not a generated storage name

#### Scenario: Comment on an attachment
- **WHEN** the composer holds text at the moment a file is uploaded
- **THEN** that text is stored as the attachment's comment and displayed with it

#### Scenario: Metadata display
- **WHEN** an attachment appears in the chat
- **THEN** its file name, size, and an image-or-file icon are shown, with the comment beneath when present

### Requirement: Inline image rendering
Attachments identified as images SHALL be rendered inline as a bounded preview in addition to being downloadable; non-image attachments SHALL be shown as a named download link. A preview that cannot be loaded SHALL degrade to the link without breaking the message.

#### Scenario: Image preview
- **WHEN** a message carries an image attachment the viewer may access
- **THEN** the image is displayed inline at a bounded height alongside its file name

#### Scenario: Preview fails
- **WHEN** the image content cannot be retrieved
- **THEN** the message still renders with the file name as a download link

### Requirement: Download access control
An attachment SHALL be downloadable only by a current member of the chat that contains it. Access SHALL be evaluated at download time, so losing membership immediately revokes access — including for the user who uploaded the file.

#### Scenario: Member downloads
- **WHEN** a current member requests an attachment from their chat
- **THEN** the file is returned with its original file name and content type

#### Scenario: Non-member downloads
- **WHEN** a user who is not a member of the containing chat requests the attachment
- **THEN** the request is refused as not-a-member

#### Scenario: Uploader who lost access
- **WHEN** a user who uploaded a file is later removed or banned from that room and requests the file
- **THEN** the request is refused

#### Scenario: Unknown attachment
- **WHEN** the requested attachment id does not exist, or its stored content is missing
- **THEN** the request returns not found

### Requirement: Attachment storage and persistence
Attachment content SHALL be stored on the local file system under a per-room directory, keyed so the original name is not needed for lookup. Files SHALL persist after upload even when the uploader later loses access to the room.

#### Scenario: File survives loss of access
- **WHEN** the uploader loses access to the room
- **THEN** the file remains in storage and remains available to the room's current members

#### Scenario: Storage layout
- **WHEN** a file is uploaded to a room
- **THEN** it is written beneath that room's directory in the configured uploads root
