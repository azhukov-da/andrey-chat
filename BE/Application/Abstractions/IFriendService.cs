using Application.Common;
using Application.Features.Friends.Dtos;

namespace Application.Abstractions;

public interface IFriendService
{
    Task<Result<List<FriendDto>>> ListFriendsAsync();
    Task<Result> SendFriendRequestAsync(string username, string? message);
    Task<Result> AcceptFriendRequestAsync(string friendUserId);
    Task<Result> RejectFriendRequestAsync(string friendUserId);
    Task<Result> RemoveFriendAsync(string friendUserId);
    Task<Result> BlockUserAsync(string userId);
    Task<Result> UnblockUserAsync(string userId);
}
