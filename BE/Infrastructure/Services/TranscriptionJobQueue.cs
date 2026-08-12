using System.Threading.Channels;
using Application.Abstractions;

namespace Infrastructure.Services;

public class TranscriptionJobQueue : ITranscriptionJobQueue
{
    private readonly Channel<TranscriptionJob> _channel = Channel.CreateUnbounded<TranscriptionJob>();

    public void Enqueue(TranscriptionJob job)
    {
        _channel.Writer.TryWrite(job);
    }

    public async Task<TranscriptionJob> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
