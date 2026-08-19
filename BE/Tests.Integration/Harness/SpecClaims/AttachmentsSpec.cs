namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>attachments</c>, mirroring
/// <c>openspec/specs/attachments/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Pasting from the clipboard, how a message row lays out name and size, inline previews and their
/// fallback, recording from the microphone, and in-message playback are all client behaviour and
/// have no constants here — the server sees an ordinary upload or an ordinary download. See
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class AttachmentsSpec
{
    private const string Uploading = "@spec:attachments/uploading-attachments/";
    private const string SizeLimits = "@spec:attachments/size-limits-by-kind/";
    private const string Metadata = "@spec:attachments/attachment-metadata/";
    private const string Download = "@spec:attachments/download-access-control/";
    private const string Storage = "@spec:attachments/attachment-storage-and-persistence/";
    private const string Transcription = "@spec:attachments/voice-attachment-transcription/";
    private const string LengthLimit = "@spec:attachments/transcription-eligibility-length-limit/";

    public const string UploadingAFile = Uploading + "uploading-a-file ";
    public const string UploadingSeveralFiles = Uploading + "uploading-several-files-at-once ";
    public const string NonMemberUpload = Uploading + "non-member-upload ";
    public const string BannedUploader = Uploading + "banned-uploader ";
    public const string EmptyFile = Uploading + "empty-file ";

    public const string OversizedImage = SizeLimits + "oversized-image ";
    public const string OversizedFile = SizeLimits + "oversized-file ";

    public const string OriginalNamePreserved = Metadata + "original-name-preserved ";
    public const string CommentOnAnAttachment = Metadata + "comment-on-an-attachment ";

    public const string MemberDownloads = Download + "member-downloads ";
    public const string NonMemberDownloads = Download + "non-member-downloads ";
    public const string UploaderWhoLostAccess = Download + "uploader-who-lost-access ";
    public const string UnknownAttachment = Download + "unknown-attachment ";

    public const string FileSurvivesLossOfAccess = Storage + "file-survives-loss-of-access ";
    public const string StorageLayout = Storage + "storage-layout ";

    public const string VoiceAttachmentGetsATranscript = Transcription + "voice-attachment-gets-a-transcript ";
    public const string TranscriptionFails = Transcription + "transcription-fails-for-a-voice-attachment ";

    public const string LongVoiceAttachment = LengthLimit + "long-voice-attachment-uploads-without-a-transcript ";
}
