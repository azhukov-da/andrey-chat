using Application.Abstractions;
using Application.Common;
using Application.Features.Rooms.Dtos;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class DirectChatService : IDirectChatService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IChatNotifier _notifier;

    public DirectChatService(IApplicationDbContext context, ICurrentUser currentUser, IChatNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<Result<RoomDto>> OpenOrCreateAsync(string username)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        if (username == _currentUser.UserName)
            return new Error("DirectChat.CannotChatWithSelf", "You cannot create a direct chat with yourself.");

        var otherUser = await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == username);

        if (otherUser == null)
            return Errors.User.NotFoundByUsername(username);

        var userIds = new[] { _currentUser.UserId, otherUser.Id }.OrderBy(id => id).ToArray();

        var isBlockedEither = await _context.UserBlocks
            .AnyAsync(b =>
                (b.BlockerId == _currentUser.UserId && b.BlockedId == otherUser.Id) ||
                (b.BlockerId == otherUser.Id && b.BlockedId == _currentUser.UserId));

        if (isBlockedEither)
            return Errors.Friendship.NotAccepted;

        var areFriends = await _context.Friendships
            .AnyAsync(f =>
                f.UserAId == userIds[0] && f.UserBId == userIds[1] &&
                f.Status == FriendshipStatus.Accepted);

        if (!areFriends)
            return Errors.Friendship.NotAccepted;

        var existingRoom = await _context.Rooms
            .Where(r => r.Kind == RoomKind.Direct && r.DeletedAt == null)
            .Where(r => r.Memberships.Count == 2 &&
                       r.Memberships.Any(m => m.UserId == userIds[0]) &&
                       r.Memberships.Any(m => m.UserId == userIds[1]))
            .Select(r => new RoomDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Visibility = r.Visibility,
                Kind = r.Kind,
                OwnerId = r.OwnerId,
                OwnerUserName = null,
                IsFrozen = r.IsFrozen,
                CreatedAt = r.CreatedAt,
                MemberCount = 2,
                MyRole = r.Memberships.Where(m => m.UserId == _currentUser.UserId).Select(m => (RoomRole?)m.Role).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (existingRoom != null)
            return existingRoom;

        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = $"{_currentUser.UserName}-{username}",
            Visibility = RoomVisibility.Private,
            Kind = RoomKind.Direct,
            IsFrozen = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Rooms.Add(room);

        var membership1 = new RoomMembership
        {
            RoomId = room.Id,
            UserId = _currentUser.UserId,
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        };

        var membership2 = new RoomMembership
        {
            RoomId = room.Id,
            UserId = otherUser.Id,
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        };

        _context.RoomMemberships.Add(membership1);
        _context.RoomMemberships.Add(membership2);

        await _context.SaveChangesAsync();

        await _notifier.AddUserToRoomGroupAsync(_currentUser.UserId, room.Id);
        await _notifier.AddUserToRoomGroupAsync(otherUser.Id, room.Id);

        return new RoomDto
        {
            Id = room.Id,
            Name = room.Name,
            Description = room.Description,
            Visibility = room.Visibility,
            Kind = room.Kind,
            OwnerId = room.OwnerId,
            OwnerUserName = null,
            IsFrozen = room.IsFrozen,
            CreatedAt = room.CreatedAt,
            MemberCount = 2,
            MyRole = RoomRole.Member
        };
    }
}
