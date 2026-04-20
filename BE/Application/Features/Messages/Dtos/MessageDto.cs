namespace Application.Features.Messages.Dtos;

public class MessageDto
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorUserName { get; set; } = string.Empty;
    public string? AuthorDisplayName { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid? ReplyToMessageId { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AttachmentMetadataDto> Attachments { get; set; } = new();
}

public class AttachmentMetadataDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Comment { get; set; }
}
