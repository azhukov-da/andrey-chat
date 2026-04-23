using Domain.Enums;

namespace Application.Features.Rooms.Dtos;

public class RoomDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoomVisibility Visibility { get; set; }
    public RoomKind Kind { get; set; }
    public string? OwnerId { get; set; }
    public string? OwnerUserName { get; set; }
    public string? OtherUserId { get; set; }
    public bool IsFrozen { get; set; }
    public DateTime CreatedAt { get; set; }
    public int MemberCount { get; set; }
    public RoomRole? MyRole { get; set; }
}
