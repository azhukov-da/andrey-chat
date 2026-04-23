using Domain.Enums;

namespace Web.Models;

public record CreateRoomRequest(string Name, string? Description, RoomVisibility Visibility);
