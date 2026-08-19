namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>speech-to-text</c>, mirroring
/// <c>openspec/specs/speech-to-text/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Dictating into the composer, appending to text already typed, the microphone permission prompt,
/// and the client-side stop at 120 seconds are all composer behaviour and have no constants here —
/// the server sees one ordinary transcription request or none at all. See
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class SpeechToTextSpec
{
    private const string Transcribing = "@spec:speech-to-text/transcribing-an-audio-clip/";
    private const string LengthLimit = "@spec:speech-to-text/dictation-length-limit/";

    public const string SuccessfulTranscription = Transcribing + "successful-transcription ";
    public const string UnauthenticatedRequest = Transcribing + "unauthenticated-request ";
    public const string EmptyOrUnreadableAudio = Transcribing + "empty-or-unreadable-audio ";
    public const string ServiceUnavailable = Transcribing + "transcription-service-unavailable ";

    public const string OversizedClipSubmittedDirectly = LengthLimit + "oversized-clip-submitted-directly ";
}
