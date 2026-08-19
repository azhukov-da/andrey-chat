using System.Collections.Concurrent;
using Application.Abstractions;
using Application.Common;

namespace Tests.Integration.Harness;

/// <summary>
/// The transcription seam, substituted in the test host.
///
/// The real <c>FasterWhisperTranscriptionService</c> posts the audio to an external model server.
/// Depending on that would break both the isolation requirement and the requirement that the suite
/// runs on a developer's machine with nothing hosted, so <see cref="ChatAppFactory"/> replaces the
/// registration with this. Everything else in the path stays real: the controller, the storage, the
/// queue, and the application's own <c>TranscriptionBackgroundService</c> all run as they ship.
///
/// The answer is scripted per test through <see cref="Responder"/>. By default every call succeeds
/// with <see cref="DefaultTranscript"/>, which is the case most attachment tests want.
/// </summary>
public sealed class FakeTranscriptionService : ITranscriptionService
{
    public const string DefaultTranscript = "a transcript produced by the test double";

    private readonly ConcurrentQueue<TranscriptionCall> _calls = new();
    private readonly ConcurrentQueue<TaskCompletionSource<TranscriptionCall>> _waiters = new();

    /// <summary>
    /// Decides the answer for a call. Set it in the arrange step to script a failure, an empty
    /// transcript, or a slow model; leave it alone for a plain success.
    /// </summary>
    public Func<TranscriptionCall, Result<TranscriptionResult>> Responder { get; set; } =
        _ => Result<TranscriptionResult>.Success(new TranscriptionResult(DefaultTranscript, "en"));

    /// <summary>Every call the application has made, oldest first.</summary>
    public IReadOnlyCollection<TranscriptionCall> Calls => _calls.ToArray();

    public async Task<Result<TranscriptionResult>> TranscribeAsync(
        Stream audio,
        string fileName,
        string contentType,
        TimeSpan? maxDuration,
        CancellationToken cancellationToken = default)
    {
        // Read the audio out rather than holding the stream: the caller disposes it, and a test
        // asserting on what was sent needs the bytes to still be there afterwards.
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, cancellationToken);

        var call = new TranscriptionCall(fileName, contentType, maxDuration, buffer.ToArray());

        // Hand the call to whoever is waiting for it before recording it, so a test that claimed
        // the call before triggering it is released the moment it happens.
        if (_waiters.TryDequeue(out var waiter)) waiter.TrySetResult(call);
        _calls.Enqueue(call);

        return Responder(call);
    }

    /// <summary>
    /// Claims the next call without waiting for it, the same claim-before-trigger shape
    /// <see cref="TestHubClient.Expect{T}"/> uses. A call already made satisfies it at once.
    /// </summary>
    public Task<TranscriptionCall> ExpectCall(TimeSpan? timeout = null)
    {
        if (_calls.TryPeek(out var alreadyMade)) return Task.FromResult(alreadyMade);

        var source = new TaskCompletionSource<TranscriptionCall>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Enqueue(source);
        return source.Task.WaitAsync(timeout ?? TestHubClient.DefaultTimeout);
    }

    /// <summary>Forgets the scripted answer and the recorded calls. Called between tests.</summary>
    public void Reset()
    {
        _calls.Clear();
        while (_waiters.TryDequeue(out var waiter)) waiter.TrySetCanceled();
        Responder = _ => Result<TranscriptionResult>.Success(
            new TranscriptionResult(DefaultTranscript, "en"));
    }
}

/// <summary>One transcription the application asked for, with the audio it sent.</summary>
public sealed record TranscriptionCall(
    string FileName, string ContentType, TimeSpan? MaxDuration, byte[] Audio);

/// <summary>
/// The application's queue, with the enqueued jobs recorded.
///
/// The delivery mechanism is deliberately the same unbounded channel the shipped
/// <c>TranscriptionJobQueue</c> uses, and the application's own background service still drains it —
/// substituting the processing would mean the tests no longer verify it. What this adds is a
/// claimable signal, so a test asserting that a voice attachment was queued for transcription (or
/// that a rejected one was not) has something to wait on instead of a sleep.
/// </summary>
public sealed class RecordingTranscriptionJobQueue : ITranscriptionJobQueue
{
    private readonly System.Threading.Channels.Channel<TranscriptionJob> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<TranscriptionJob>();

    private readonly ConcurrentQueue<TranscriptionJob> _enqueued = new();
    private readonly ConcurrentQueue<TaskCompletionSource<TranscriptionJob>> _waiters = new();

    /// <summary>Every job the application has queued, oldest first.</summary>
    public IReadOnlyCollection<TranscriptionJob> Enqueued => _enqueued.ToArray();

    public void Enqueue(TranscriptionJob job)
    {
        if (_waiters.TryDequeue(out var waiter)) waiter.TrySetResult(job);
        _enqueued.Enqueue(job);

        _channel.Writer.TryWrite(job);
    }

    public async Task<TranscriptionJob> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);

    /// <summary>Claims the next queued job without waiting for it.</summary>
    public Task<TranscriptionJob> ExpectJob(TimeSpan? timeout = null)
    {
        if (_enqueued.TryPeek(out var already)) return Task.FromResult(already);

        var source = new TaskCompletionSource<TranscriptionJob>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Enqueue(source);
        return source.Task.WaitAsync(timeout ?? TestHubClient.DefaultTimeout);
    }

    /// <summary>Forgets the recorded jobs. Called between tests.</summary>
    public void Reset()
    {
        _enqueued.Clear();
        while (_waiters.TryDequeue(out var waiter)) waiter.TrySetCanceled();
        while (_channel.Reader.TryRead(out _)) { }
    }
}
