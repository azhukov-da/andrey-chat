using Application.Common;
using Application.Features.Rooms.Dtos;
using Domain.Enums;

namespace Application.Abstractions;

public interface IRoomService
{
    Task<Result<Paged<RoomDto>>> ListPublicRoomsAsync(string? search, int page, int pageSize);
    Task<Result<List<RoomDto>>> ListMyRoomsAsync();
    Task<Result<RoomDto>> GetRoomAsync(Guid roomId);
    Task<Result<RoomDto>> CreateRoomAsync(string name, string? description, RoomVisibility visibility);
    Task<Result> JoinRoomAsync(Guid roomId);
    Task<Result> DeleteRoomAsync(Guid roomId);
}
