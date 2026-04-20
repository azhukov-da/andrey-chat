using Domain.Enums;

namespace Domain.Entities;

public class Attachment
{
    public Guid Id { get; set; }
    
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public AttachmentKind Kind { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}
