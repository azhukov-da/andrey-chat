using Application.Abstractions;

namespace Infrastructure.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _uploadsRoot;

    public LocalFileStorage(string uploadsRoot)
    {
        _uploadsRoot = uploadsRoot;
        Directory.CreateDirectory(_uploadsRoot);
    }

    public async Task<string> SaveFileAsync(
        Stream stream,
        string fileName,
        string contentType,
        Guid roomId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var roomDirectory = Path.Combine(_uploadsRoot, roomId.ToString());
        Directory.CreateDirectory(roomDirectory);

        var extension = Path.GetExtension(fileName);
        var storagePath = Path.Combine(roomId.ToString(), $"{attachmentId}{extension}");
        var fullPath = Path.Combine(_uploadsRoot, storagePath);

        using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return storagePath;
    }

    public Task<Stream> GetFileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_uploadsRoot, storagePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File not found", storagePath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_uploadsRoot, storagePath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public bool FileExists(string storagePath)
    {
        var fullPath = Path.Combine(_uploadsRoot, storagePath);
        return File.Exists(fullPath);
    }
}
