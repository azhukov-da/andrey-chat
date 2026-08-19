using Application.Abstractions;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.Attachments;

/// <summary>
/// Guards the substitution itself, rather than any specified scenario — so these carry no
/// <c>@spec:</c> claim.
///
/// The real transcriber posts audio to a faster-whisper server over HTTP. If the substitution in
/// <see cref="ChatAppFactory"/> ever stopped taking effect, the attachment tests would not fail
/// loudly: on a machine with nothing on that address they would go slow and then fail obscurely,
/// and on a machine that happened to be running one they would pass while depending on what a model
/// returned. Either way the failure would not name its cause. These tests do.
/// </summary>
[Collection(Collections.Media)]
public class TranscriptionSeamTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = "the host resolves the transcription double, never the faster-whisper client")]
    public async Task The_real_transcription_service_is_not_registered()
    {
        var resolved = await App.WithScopeAsync(services =>
            Task.FromResult(services.GetRequiredService<ITranscriptionService>()));

        resolved.Should().BeSameAs(App.Transcription);
        resolved.Should().NotBeOfType<FasterWhisperTranscriptionService>(
            "a run that reached the real service would depend on something hosted");
    }

    [Fact(DisplayName = "the host resolves the recording job queue, and the application's own background service drains it")]
    public async Task The_recording_queue_is_registered_and_still_drained_by_the_application()
    {
        var resolved = await App.WithScopeAsync(services =>
            Task.FromResult(services.GetRequiredService<ITranscriptionJobQueue>()));

        resolved.Should().BeSameAs(App.TranscriptionQueue);

        App.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should().ContainItemsAssignableTo<TranscriptionBackgroundService>(
                "substituting the queue must not take the application's own processing out of the path");
    }

    [Fact(DisplayName = "a queued job reaches the transcription seam through the application's background service")]
    public async Task A_queued_job_is_processed_by_the_application()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var attachmentId = Guid.NewGuid();
        var storagePath = await WriteAudioAsync(room.Id, attachmentId, "recording.webm", [1, 2, 3, 4]);

        var call = App.Transcription.ExpectCall();

        App.TranscriptionQueue.Enqueue(new TranscriptionJob(
            AttachmentId: attachmentId,
            RoomId: room.Id,
            StoragePath: storagePath,
            ContentType: "audio/webm",
            FileName: "recording.webm"));

        var recorded = await call;
        recorded.FileName.Should().Be("recording.webm");
        recorded.ContentType.Should().Be("audio/webm");
        recorded.Audio.Should().Equal([1, 2, 3, 4],
            "the bytes the seam sees are the ones the application read back out of storage");
        recorded.MaxDuration.Should().NotBeNull(
            "the application caps how long a clip it will transcribe");
    }

    /// <summary>Writes a file through the application's own storage and returns its storage path.</summary>
    private Task<string> WriteAudioAsync(Guid roomId, Guid attachmentId, string fileName, byte[] bytes) =>
        App.WithScopeAsync(async services =>
        {
            using var stream = new MemoryStream(bytes);
            return await services.GetRequiredService<IFileStorage>()
                .SaveFileAsync(stream, fileName, "audio/webm", roomId, attachmentId);
        });
}
