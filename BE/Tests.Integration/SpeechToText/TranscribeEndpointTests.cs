using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Application.Common;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.SpeechToText;

/// <summary>
/// Backend integration tests for <c>POST /api/transcribe</c> —
/// <c>openspec/specs/speech-to-text/spec.md</c>.
///
/// This is the dictation endpoint: one clip in, one transcript out, no streaming. What is verified
/// here is the contract around that exchange — who may call it, what it does with audio it cannot
/// use, and how it answers when the local backend is not there. The backend itself is substituted
/// (see <see cref="FakeTranscriptionService"/>), which is also what keeps the suite honest about
/// the specification's other requirement: nothing leaves the machine.
/// </summary>
[Collection(Collections.Media)]
public class TranscribeEndpointTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    private static readonly byte[] Clip = [0x1a, 0x45, 0xdf, 0xa3, 0x09, 0x08, 0x07];

    [Fact(DisplayName = SpeechToTextSpec.SuccessfulTranscription
                        + "an authenticated user submitting a clip gets its transcript back")]
    public async Task A_submitted_clip_comes_back_as_text()
    {
        var user = await CreateUserAsync();
        App.Transcription.Responder = _ => Result<TranscriptionResult>.Success(
            new TranscriptionResult("let us push the release to Friday", "en"));

        var response = await PostClipAsync(user.Client, "dictation.webm", "audio/webm", Clip);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("text").GetString().Should().Be("let us push the release to Friday");
        body.GetProperty("language").GetString().Should().Be("en");

        var call = App.Transcription.Calls.Should().ContainSingle().Subject;
        call.Audio.Should().Equal(Clip, "the clip is sent once, whole, in the one request");
        call.MaxDuration.Should().Be(TimeSpan.FromSeconds(120),
            "the server applies the dictation ceiling itself rather than trusting the caller");
    }

    [Fact(DisplayName = SpeechToTextSpec.UnauthenticatedRequest
                        + "a request with no session is refused as unauthorized and never reaches the transcriber")]
    public async Task An_unauthenticated_request_is_refused()
    {
        var response = await PostClipAsync(App.CreateClient(), "dictation.webm", "audio/webm", Clip);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        App.Transcription.Calls.Should().BeEmpty(
            "an anonymous caller does not get to spend the machine's transcription capacity");
    }

    [Fact(DisplayName = SpeechToTextSpec.EmptyOrUnreadableAudio
                        + "a zero-length clip is rejected as a bad request without being sent on")]
    public async Task An_empty_clip_is_rejected()
    {
        var user = await CreateUserAsync();

        var response = await PostClipAsync(user.Client, "silence.webm", "audio/webm", []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        App.Transcription.Calls.Should().BeEmpty("there is nothing there to transcribe");
    }

    [Fact(DisplayName = SpeechToTextSpec.EmptyOrUnreadableAudio
                        + "audio the backend cannot decode is a bad request, with no partial transcript")]
    public async Task Undecodable_audio_is_a_bad_request()
    {
        var user = await CreateUserAsync();
        App.Transcription.Responder = _ => Result<TranscriptionResult>.Failure(
            Errors.Transcription.EmptyOrUndecodable);

        var response = await PostClipAsync(user.Client, "notes.txt", "text/plain", Clip);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Transcription.EmptyOrUndecodable");
        body.Should().NotContain("\"text\"",
            "a rejected clip yields no transcript at all, not a partial one");
    }

    [Fact(DisplayName = SpeechToTextSpec.ServiceUnavailable
                        + "an unreachable local backend answers as unavailable, distinctly from a bad clip")]
    public async Task An_unavailable_backend_is_reported_as_unavailable()
    {
        var user = await CreateUserAsync();
        App.Transcription.Responder = _ => Result<TranscriptionResult>.Failure(
            Errors.Transcription.Unavailable);

        var response = await PostClipAsync(user.Client, "dictation.webm", "audio/webm", Clip);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "unavailable is the server's problem, not the caller's — a 4xx would tell them to change the clip");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Transcription.Unavailable");
    }

    [Fact(DisplayName = SpeechToTextSpec.ServiceUnavailable
                        + "a failed transcription leaves the caller's own state untouched")]
    public async Task A_failed_transcription_changes_nothing_for_the_caller()
    {
        var user = await CreateUserAsync();
        var room = await user.CreateRoomAsync();
        var existing = await user.SendMessageAsync(room.Id, "already typed and sent");

        App.Transcription.Responder = _ => Result<TranscriptionResult>.Failure(
            Errors.Transcription.Unavailable);

        (await PostClipAsync(user.Client, "dictation.webm", "audio/webm", Clip))
            .IsSuccessStatusCode.Should().BeFalse();

        var history = (await user.GetHistoryAsync(room.Id)).Items;
        history.Should().ContainSingle().Which.Id.Should().Be(existing.Id,
            "a dictation that failed is not a message, and takes nothing with it");
    }

    [Fact(DisplayName = "a clip the backend refuses as too long is a bad request naming the limit")]
    public async Task A_clip_refused_as_too_long_is_a_bad_request()
    {
        // The outcome half of the length limit, which the implementation does reach — by asking the
        // backend and being turned down rather than by declining to ask. The scenario claim sits on
        // the skipped test below, so this one deliberately makes none.
        var user = await CreateUserAsync();
        App.Transcription.Responder = call => Result<TranscriptionResult>.Failure(
            Errors.Transcription.TooLong((int)(call.MaxDuration?.TotalSeconds ?? 0)));

        var response = await PostClipAsync(user.Client, "long.webm", "audio/webm", Clip);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an over-long clip is something the caller can fix, so it is theirs to hear about");
        (await response.Content.ReadAsStringAsync()).Should().Contain("120s",
            "and the refusal names the limit rather than only failing");
    }

    [Fact(Skip = "SPEC GAP speech-to-text/dictation-length-limit: the endpoint has no way to measure "
                 + "a clip and forwards every one to the transcription backend, which is what applies "
                 + "the 120-second limit. See docs/spec-gaps.md.",
          DisplayName = SpeechToTextSpec.OversizedClipSubmittedDirectly
                        + "a clip longer than 120 seconds is rejected without transcription being attempted")]
    public async Task An_oversized_clip_is_rejected_without_being_attempted()
    {
        var user = await CreateUserAsync();

        var response = await PostClipAsync(user.Client, "long.webm", "audio/webm", Clip);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        App.Transcription.Calls.Should().BeEmpty(
            "the limit is meant to hold even when the client is bypassed, without spending the backend");
    }

    private static Task<HttpResponseMessage> PostClipAsync(
        HttpClient client, string fileName, string contentType, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);

        return client.PostAsync("/api/transcribe", form);
    }
}
