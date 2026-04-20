using Domain.Enums;

namespace Domain.Entities;

public class RoomInvitation
{
    public Guid Id { get; set; }
    
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    
    public string InvitedUserId { get; set; } = string.Empty;
    public ApplicationUser InvitedUser { get; set; } = null!;
    
    public string InvitedByUserId { get; set; } = string.Empty;
    public ApplicationUser InvitedByUser { get; set; } = null!;
    
    public InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
