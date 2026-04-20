namespace Domain.Entities;

public class RoomBan
{
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    
    public string BannedUserId { get; set; } = string.Empty;
    public ApplicationUser BannedUser { get; set; } = null!;
    
    public string BannedByUserId { get; set; } = string.Empty;
    public ApplicationUser BannedByUser { get; set; } = null!;
    
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
