namespace Application.Abstractions;

public record TranscriptionJob(Guid AttachmentId, Guid RoomId, string StoragePath, string ContentType, string FileName);

public interface ITranscriptionJobQueue
{
    void Enqueue(TranscriptionJob job);
    Task<TranscriptionJob> DequeueAsync(CancellationToken cancellationToken);
}
