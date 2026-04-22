namespace Application.Features.Rooms.Dtos;

public class RoomBanDto
{
    public string BannedUserId { get; set; } = string.Empty;
    public string BannedUserName { get; set; } = string.Empty;
    public string BannedByUserId { get; set; } = string.Empty;
    public string BannedByUserName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
