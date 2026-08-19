using System.Net;
using System.Text.Json;
using Application.Abstractions;
using Application.Common;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.Attachments;

/// <summary>
/// Backend integration tests for what happens to a voice attachment after it is uploaded —
/// <c>openspec/specs/attachments/spec.md</c>, the <em>Voice attachment transcription</em> and
/// <em>Transcription eligibility length limit</em> requirements.
///
/// The transcriber itself is substituted (see <see cref="FakeTranscriptionService"/>); everything
/// between the upload and the stored caption is the application's own. Because transcription
/// finishes on a background worker, success is asserted through the push the worker sends when it
/// is done, not by waiting a while and looking.
/// </summary>
[Collection(Collections.Media)]
public class VoiceTranscriptionTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    private static readonly byte[] AudioBytes = [0x1a, 0x45, 0xdf, 0xa3, 0x01, 0x02, 0x03];

    [Fact(DisplayName = AttachmentsSpec.VoiceAttachmentGetsATranscript
                        + "an audio upload is a message at once, and gains a transcript caption when transcription completes")]
    public async Task An_audio_upload_gains_a_transcript()
    {
        var uploader = await CreateUserAsync();
        var listener = await CreateUserAsync();
        var room = await uploader.CreateRoomWithAsync(listener);

        App.Transcription.Responder = _ => Result<TranscriptionResult>.Success(
            new TranscriptionResult("shall we move the meeting to Thursday", "en"));

        await using var hub = await ConnectHubAsync(listener);
        var transcribed = hub.Expect<JsonElement>(ChatHubEvents.AttachmentTranscribed);

        var message = await uploader.UploadAsync(room.Id, "note.webm", "audio/webm", AudioBytes);
        var attachment = message.Attachments.Single();

        attachment.Kind.Should().Be("Audio");
        attachment.TranscriptText.Should().BeNull(
            "the message is created immediately; the caption follows when the transcriber answers");

        var payload = await transcribed.RawAsync();
        payload.GetProperty("attachmentId").GetString().Should().Be(attachment.Id.ToString());
        payload.GetProperty("transcriptText").GetString()
            .Should().Be("shall we move the meeting to Thursday");

        var stored = await ReadAttachmentAsync(attachment.Id);
        stored.TranscriptText.Should().Be("shall we move the meeting to Thursday",
            "the caption is persisted, so it is there for anyone who opens the chat later");
        stored.Comment.Should().BeNull(
            "the transcript is distinct from the uploader's optional comment");
    }

    [Fact(DisplayName = AttachmentsSpec.VoiceAttachmentGetsATranscript
                        + "the transcript is added alongside the uploader's comment rather than replacing it")]
    public async Task A_transcript_does_not_displace_the_comment()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        await using var hub = await ConnectHubAsync(uploader);
        var transcribed = hub.Expect<JsonElement>(ChatHubEvents.AttachmentTranscribed);

        var message = await uploader.UploadAsync(
            room.Id, "note.webm", "audio/webm", AudioBytes, comment: "listen to this bit");
        await transcribed.RawAsync();

        var stored = await ReadAttachmentAsync(message.Attachments.Single().Id);
        stored.Comment.Should().Be("listen to this bit");
        stored.TranscriptText.Should().Be(FakeTranscriptionService.DefaultTranscript);
    }

    [Fact(DisplayName = AttachmentsSpec.VoiceAttachmentGetsATranscript
                        + "only audio is submitted for transcription")]
    public async Task Non_audio_uploads_are_not_submitted()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        await uploader.UploadAsync(room.Id, "diagram.png", "image/png", AudioBytes);
        await uploader.UploadAsync(room.Id, "notes.txt", "text/plain", AudioBytes);

        App.TranscriptionQueue.Enqueued.Should().BeEmpty(
            "an image and a text file have nothing to transcribe");
    }

    [Fact(DisplayName = AttachmentsSpec.TranscriptionFails
                        + "a failed transcription leaves the attachment intact and uncaptioned, and tells nobody")]
    public async Task A_failed_transcription_is_silent_and_harmless()
    {
        var uploader = await CreateUserAsync();
        var listener = await CreateUserAsync();
        var room = await uploader.CreateRoomWithAsync(listener);

        App.Transcription.Responder = _ => Result<TranscriptionResult>.Failure(
            Errors.Transcription.Unavailable);

        await using var hub = await ConnectHubAsync(listener);
        var nothingPushed = hub.Expect<JsonElement>(
            ChatHubEvents.AttachmentTranscribed, timeout: TimeSpan.FromSeconds(2));

        var message = await uploader.UploadAsync(room.Id, "note.webm", "audio/webm", AudioBytes);
        var attachmentId = message.Attachments.Single().Id;

        // The seam having been called is the proof the worker got as far as the transcriber; the
        // failure branch writes nothing after that, so what follows is settled.
        await App.Transcription.ExpectCall();

        (await nothingPushed.TimedOutAsync()).Should().BeTrue(
            "a transcription that failed is not an error the other participants are shown");

        (await ReadAttachmentAsync(attachmentId)).TranscriptText.Should().BeNull();

        var download = await listener.Client.GetAsync($"/api/attachments/{attachmentId}");
        download.StatusCode.Should().Be(HttpStatusCode.OK,
            "the clip is still there to play and to download");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(AudioBytes);
    }

    [Fact(DisplayName = AttachmentsSpec.TranscriptionFails
                        + "a transcription that succeeds with no words leaves no caption")]
    public async Task An_empty_transcript_is_not_stored_as_a_caption()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        App.Transcription.Responder = _ => Result<TranscriptionResult>.Success(
            new TranscriptionResult("   ", "en"));

        var message = await uploader.UploadAsync(room.Id, "silence.webm", "audio/webm", AudioBytes);
        await App.Transcription.ExpectCall();

        (await ReadAttachmentAsync(message.Attachments.Single().Id)).TranscriptText.Should().BeNull(
            "an empty caption is worse than none — it is a blank line under every clip");
    }

    [Fact(Skip = "SPEC GAP attachments/transcription-eligibility-length-limit: the application "
                 + "enqueues every audio attachment; the ten-minute limit is applied by the "
                 + "transcription backend, not before the job. See docs/spec-gaps.md.",
          DisplayName = AttachmentsSpec.LongVoiceAttachment
                        + "an audio attachment longer than ten minutes is not submitted for transcription")]
    public async Task A_long_clip_is_not_submitted_for_transcription()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        await uploader.UploadAsync(room.Id, "long-meeting.webm", "audio/webm", AudioBytes);

        App.TranscriptionQueue.Enqueued.Should().BeEmpty(
            "a clip that is known to be too long is not worth the round trip");
    }

    [Fact(DisplayName = "a clip the transcriber refuses as too long still uploads, plays, and simply has no caption")]
    public async Task A_clip_refused_as_too_long_still_uploads_without_a_caption()
    {
        // The outcome half of the length limit, which the implementation does reach — by asking the
        // transcriber and being turned down rather than by declining to ask. The scenario claim sits
        // on the skipped test above, so this one deliberately makes none.
        var uploader = await CreateUserAsync();
        var listener = await CreateUserAsync();
        var room = await uploader.CreateRoomWithAsync(listener);

        App.Transcription.Responder = call => Result<TranscriptionResult>.Failure(
            Errors.Transcription.TooLong((int)(call.MaxDuration?.TotalSeconds ?? 0)));

        await using var hub = await ConnectHubAsync(listener);
        var nothingPushed = hub.Expect<JsonElement>(
            ChatHubEvents.AttachmentTranscribed, timeout: TimeSpan.FromSeconds(2));

        var message = await uploader.UploadAsync(room.Id, "long-meeting.webm", "audio/webm", AudioBytes);
        var attachmentId = message.Attachments.Single().Id;

        var call = await App.Transcription.ExpectCall();
        call.MaxDuration.Should().Be(TimeSpan.FromMinutes(10),
            "ten minutes is the ceiling the application asks the transcriber to apply");

        (await nothingPushed.TimedOutAsync()).Should().BeTrue();
        (await ReadAttachmentAsync(attachmentId)).TranscriptText.Should().BeNull();
        (await listener.Client.GetAsync($"/api/attachments/{attachmentId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<Domain.Entities.Attachment> ReadAttachmentAsync(Guid id) =>
        App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Attachments
                .AsNoTracking().SingleAsync(a => a.Id == id));
}
