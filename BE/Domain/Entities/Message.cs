namespace Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    
    public string AuthorId { get; set; } = string.Empty;
    public ApplicationUser Author { get; set; } = null!;
    
    public string Text { get; set; } = string.Empty;
    
    public Guid? ReplyToMessageId { get; set; }
    public Message? ReplyToMessage { get; set; }
    
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
