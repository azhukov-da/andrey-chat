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
    Task<Result<RoomDto>> UpdateRoomAsync(Guid roomId, string name, string? description, RoomVisibility visibility);
    Task<Result> DeleteRoomAsync(Guid roomId);
    Task<Result<List<RoomMemberDto>>> ListMembersAsync(Guid roomId);
    Task<Result> MakeAdminAsync(Guid roomId, string targetUserId);
    Task<Result> RemoveAdminAsync(Guid roomId, string targetUserId);
    Task<Result> RemoveMemberAsync(Guid roomId, string targetUserId);
    Task<Result> BanMemberAsync(Guid roomId, string targetUserId, string? reason);
    Task<Result> UnbanMemberAsync(Guid roomId, string targetUserId);
    Task<Result<List<RoomBanDto>>> ListBannedAsync(Guid roomId);
    Task<Result> LeaveRoomAsync(Guid roomId);
    Task<Result<RoomInvitationDto>> InviteUserAsync(Guid roomId, string inviteeUsername);
    Task<Result<List<RoomInvitationDto>>> ListMyInvitationsAsync();
    Task<Result> AcceptInvitationAsync(Guid invitationId);
    Task<Result> RejectInvitationAsync(Guid invitationId);
}
