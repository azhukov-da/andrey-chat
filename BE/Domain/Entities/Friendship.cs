using Domain.Enums;

namespace Domain.Entities;

public class Friendship
{
    public string UserAId { get; set; } = string.Empty;
    public ApplicationUser UserA { get; set; } = null!;
    
    public string UserBId { get; set; } = string.Empty;
    public ApplicationUser UserB { get; set; } = null!;
    
    public FriendshipStatus Status { get; set; }
    
    public string RequestedByUserId { get; set; } = string.Empty;
    public ApplicationUser RequestedByUser { get; set; } = null!;
    
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
