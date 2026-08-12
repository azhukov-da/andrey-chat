using Application.Common;

namespace Application.Abstractions;

public record TranscriptionResult(string Text, string? Language);

public interface ITranscriptionService
{
    Task<Result<TranscriptionResult>> TranscribeAsync(
        Stream audio,
        string fileName,
        string contentType,
        TimeSpan? maxDuration,
        CancellationToken cancellationToken = default);
}
