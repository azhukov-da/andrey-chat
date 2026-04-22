using Domain.Enums;

namespace Application.Features.Rooms.Dtos;

public class RoomInvitationDto
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string InvitedUserId { get; set; } = string.Empty;
    public string InvitedUserName { get; set; } = string.Empty;
    public string InvitedByUserId { get; set; } = string.Empty;
    public string InvitedByUserName { get; set; } = string.Empty;
    public InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
