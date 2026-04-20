using Domain.Enums;

namespace Application.Features.Rooms.Dtos;

public class RoomMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public RoomRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}
