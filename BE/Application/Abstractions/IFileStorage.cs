namespace Application.Abstractions;

public interface IFileStorage
{
    Task<string> SaveFileAsync(Stream stream, string fileName, string contentType, Guid roomId, Guid attachmentId, CancellationToken cancellationToken = default);
    Task<Stream> GetFileAsync(string storagePath, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default);
    bool FileExists(string storagePath);
}
