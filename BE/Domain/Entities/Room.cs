using Domain.Enums;

namespace Domain.Entities;

public class Room
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoomVisibility Visibility { get; set; }
    public RoomKind Kind { get; set; }
    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public bool IsFrozen { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<RoomMembership> Memberships { get; set; } = new List<RoomMembership>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<RoomBan> Bans { get; set; } = new List<RoomBan>();
    public ICollection<RoomInvitation> Invitations { get; set; } = new List<RoomInvitation>();
}
