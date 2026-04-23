using Application.Abstractions;
using Application.Common;
using Application.Features.Friends.Dtos;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class FriendService : IFriendService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IChatNotifier _notifier;

    public FriendService(IApplicationDbContext context, ICurrentUser currentUser, IChatNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<Result<List<FriendDto>>> ListFriendsAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var friends = await _context.Friendships
            .Where(f =>
                (f.UserAId == _currentUser.UserId || f.UserBId == _currentUser.UserId) &&
                (f.Status == FriendshipStatus.Accepted ||
                 (f.Status == FriendshipStatus.Pending && f.RequestedByUserId != _currentUser.UserId)))
            .Select(f => new FriendDto
            {
                UserId = f.UserAId == _currentUser.UserId ? f.UserBId : f.UserAId,
                UserName = f.UserAId == _currentUser.UserId ? f.UserB.UserName! : f.UserA.UserName!,
                DisplayName = f.UserAId == _currentUser.UserId ? f.UserB.DisplayName : f.UserA.DisplayName,
                Status = f.Status,
                CreatedAt = f.CreatedAt,
                AcceptedAt = f.AcceptedAt
            })
            .ToListAsync();

        return friends;
    }

    public async Task<Result> SendFriendRequestAsync(string username, string? message)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        if (username == _currentUser.UserName)
            return Errors.Friendship.CannotRequestSelf;

        var targetUser = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == username);

        if (targetUser == null)
            return Errors.User.NotFoundByUsername(username);

        var userIds = new[] { _currentUser.UserId, targetUser.Id }.OrderBy(id => id).ToArray();

        var existingFriendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.UserAId == userIds[0] && f.UserBId == userIds[1]);

        if (existingFriendship != null)
        {
            if (existingFriendship.Status == FriendshipStatus.Accepted)
                return Errors.Friendship.AlreadyFriends;
            else
                return Errors.Friendship.RequestAlreadyExists;
        }

        var friendship = new Friendship
        {
            UserAId = userIds[0],
            UserBId = userIds[1],
            Status = FriendshipStatus.Pending,
            RequestedByUserId = _currentUser.UserId,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        _context.Friendships.Add(friendship);
        await _context.SaveChangesAsync();

        await _notifier.FriendRequestReceivedAsync(targetUser.Id, new
        {
            userId = _currentUser.UserId,
            userName = _currentUser.UserName,
            message
        });

        return Result.Success();
    }

    public async Task<Result> AcceptFriendRequestAsync(string friendUserId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var userIds = new[] { _currentUser.UserId, friendUserId }.OrderBy(id => id).ToArray();

        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.UserAId == userIds[0] && f.UserBId == userIds[1]);

        if (friendship == null)
            return Errors.Friendship.NotFound;

        if (friendship.Status == FriendshipStatus.Accepted)
            return Errors.Friendship.AlreadyFriends;

        if (friendship.RequestedByUserId == _currentUser.UserId)
            return new Error("Friendship.CannotAcceptOwnRequest", "You cannot accept your own friend request.");

        friendship.Status = FriendshipStatus.Accepted;
        friendship.AcceptedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> RejectFriendRequestAsync(string friendUserId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var userIds = new[] { _currentUser.UserId, friendUserId }.OrderBy(id => id).ToArray();

        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.UserAId == userIds[0] && f.UserBId == userIds[1]);

        if (friendship == null)
            return Errors.Friendship.NotFound;

        if (friendship.Status != FriendshipStatus.Pending)
            return Errors.Friendship.NotFound;

        if (friendship.RequestedByUserId == _currentUser.UserId)
            return Errors.Friendship.CannotRejectOwnRequest;

        _context.Friendships.Remove(friendship);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> RemoveFriendAsync(string friendUserId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var userIds = new[] { _currentUser.UserId, friendUserId }.OrderBy(id => id).ToArray();

        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.UserAId == userIds[0] && f.UserBId == userIds[1]);

        if (friendship == null)
            return Errors.Friendship.NotFound;

        _context.Friendships.Remove(friendship);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> BlockUserAsync(string userId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        if (userId == _currentUser.UserId)
            return Errors.Block.CannotBlockSelf;

        var alreadyBlocked = await _context.UserBlocks
            .AnyAsync(b => b.BlockerId == _currentUser.UserId && b.BlockedId == userId);

        if (alreadyBlocked)
            return Errors.Block.AlreadyBlocked;

        var block = new UserBlock
        {
            BlockerId = _currentUser.UserId,
            BlockedId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserBlocks.Add(block);

        var directRooms = await _context.Rooms
            .Where(r => r.Kind == RoomKind.Direct && r.DeletedAt == null)
            .Where(r => r.Memberships.Any(m => m.UserId == _currentUser.UserId) &&
                       r.Memberships.Any(m => m.UserId == userId))
            .ToListAsync();

        foreach (var room in directRooms)
        {
            room.IsFrozen = true;
        }

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> UnblockUserAsync(string userId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var block = await _context.UserBlocks
            .FirstOrDefaultAsync(b => b.BlockerId == _currentUser.UserId && b.BlockedId == userId);

        if (block == null)
            return Errors.Block.NotBlocked;

        _context.UserBlocks.Remove(block);

        var directRooms = await _context.Rooms
            .Where(r => r.Kind == RoomKind.Direct && r.DeletedAt == null)
            .Where(r => r.Memberships.Any(m => m.UserId == _currentUser.UserId) &&
                       r.Memberships.Any(m => m.UserId == userId))
            .ToListAsync();

        foreach (var room in directRooms)
        {
            room.IsFrozen = false;
        }

        await _context.SaveChangesAsync();

        return Result.Success();
    }
}
