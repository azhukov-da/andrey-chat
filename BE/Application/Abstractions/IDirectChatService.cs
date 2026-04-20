using Application.Common;
using Application.Features.Rooms.Dtos;

namespace Application.Abstractions;

public interface IDirectChatService
{
    Task<Result<RoomDto>> OpenOrCreateAsync(string username);
}
