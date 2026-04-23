using Domain.Enums;

namespace Web.Models;

public record UpdateRoomRequest(string Name, string? Description, RoomVisibility Visibility);
