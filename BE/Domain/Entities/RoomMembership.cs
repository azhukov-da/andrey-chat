using Domain.Enums;

namespace Domain.Entities;

public class RoomMembership
{
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    
    public RoomRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public Guid? LastReadMessageId { get; set; }
}
