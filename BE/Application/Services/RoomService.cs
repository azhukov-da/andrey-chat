using Application.Abstractions;
using Application.Common;
using Application.Features.Rooms.Dtos;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class RoomService : IRoomService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IChatNotifier _notifier;

    public RoomService(IApplicationDbContext context, ICurrentUser currentUser, IChatNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<Result<Paged<RoomDto>>> ListPublicRoomsAsync(string? search, int page, int pageSize)
    {
        var query = _context.Rooms
            .Where(r => r.Visibility == RoomVisibility.Public && r.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(r => r.Name.ToLower().Contains(searchLower) || (r.Description != null && r.Description.ToLower().Contains(searchLower)));
        }

        var totalCount = await query.CountAsync();

        var rooms = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RoomDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Visibility = r.Visibility,
                Kind = r.Kind,
                OwnerId = r.OwnerId,
                OwnerUserName = r.Owner != null ? r.Owner.UserName : null,
                IsFrozen = r.IsFrozen,
                CreatedAt = r.CreatedAt,
                MemberCount = r.Memberships.Count,
                MyRole = _currentUser.UserId != null
                    ? r.Memberships.Where(m => m.UserId == _currentUser.UserId).Select(m => (RoomRole?)m.Role).FirstOrDefault()
                    : null
            })
            .ToListAsync();

        return new Paged<RoomDto>
        {
            Items = rooms,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<Result<List<RoomDto>>> ListMyRoomsAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var rooms = await _context.RoomMemberships
            .Where(m => m.UserId == _currentUser.UserId)
            .Where(m => m.Room.DeletedAt == null)
            .Select(m => new RoomDto
            {
                Id = m.Room.Id,
                Name = m.Room.Name,
                Description = m.Room.Description,
                Visibility = m.Room.Visibility,
                Kind = m.Room.Kind,
                OwnerId = m.Room.OwnerId,
                OwnerUserName = m.Room.Owner != null ? m.Room.Owner.UserName : null,
                IsFrozen = m.Room.IsFrozen,
                CreatedAt = m.Room.CreatedAt,
                MemberCount = m.Room.Memberships.Count,
                MyRole = m.Role
            })
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return rooms;
    }

    public async Task<Result<RoomDto>> GetRoomAsync(Guid roomId)
    {
        var room = await _context.Rooms
            .Where(r => r.Id == roomId && r.DeletedAt == null)
            .Select(r => new
            {
                Room = r,
                MemberCount = r.Memberships.Count,
                OwnerUserName = r.Owner != null ? r.Owner.UserName : null,
                MyRole = _currentUser.UserId != null
                    ? r.Memberships.Where(m => m.UserId == _currentUser.UserId).Select(m => (RoomRole?)m.Role).FirstOrDefault()
                    : null
            })
            .FirstOrDefaultAsync();

        if (room == null)
            return Errors.Room.NotFound(roomId);

        if (room.Room.Visibility == RoomVisibility.Private && room.MyRole == null)
            return Errors.Room.PrivateRoom;

        return new RoomDto
        {
            Id = room.Room.Id,
            Name = room.Room.Name,
            Description = room.Room.Description,
            Visibility = room.Room.Visibility,
            Kind = room.Room.Kind,
            OwnerId = room.Room.OwnerId,
            OwnerUserName = room.OwnerUserName,
            IsFrozen = room.Room.IsFrozen,
            CreatedAt = room.Room.CreatedAt,
            MemberCount = room.MemberCount,
            MyRole = room.MyRole
        };
    }

    public async Task<Result<RoomDto>> CreateRoomAsync(string name, string? description, RoomVisibility visibility)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var nameExists = await _context.Rooms
            .AnyAsync(r => r.Name == name && r.Kind == RoomKind.Group && r.DeletedAt == null);

        if (nameExists)
            return Errors.Room.NameAlreadyExists;

        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Visibility = visibility,
            Kind = RoomKind.Group,
            OwnerId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Rooms.Add(room);

        var membership = new RoomMembership
        {
            RoomId = room.Id,
            UserId = _currentUser.UserId,
            Role = RoomRole.Owner,
            JoinedAt = DateTime.UtcNow
        };

        _context.RoomMemberships.Add(membership);

        await _context.SaveChangesAsync();

        return new RoomDto
        {
            Id = room.Id,
            Name = room.Name,
            Description = room.Description,
            Visibility = room.Visibility,
            Kind = room.Kind,
            OwnerId = room.OwnerId,
            OwnerUserName = _currentUser.UserName,
            IsFrozen = room.IsFrozen,
            CreatedAt = room.CreatedAt,
            MemberCount = 1,
            MyRole = RoomRole.Owner
        };
    }

    public async Task<Result> JoinRoomAsync(Guid roomId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var room = await _context.Rooms
            .Include(r => r.Memberships)
            .FirstOrDefaultAsync(r => r.Id == roomId && r.DeletedAt == null);

        if (room == null)
            return Errors.Room.NotFound(roomId);

        if (room.Visibility == RoomVisibility.Private)
            return Errors.Room.PrivateRoom;

        var isBanned = await _context.RoomBans
            .AnyAsync(b => b.RoomId == roomId && b.BannedUserId == _currentUser.UserId);

        if (isBanned)
            return Errors.Room.Banned;

        var alreadyMember = room.Memberships.Any(m => m.UserId == _currentUser.UserId);
        if (alreadyMember)
            return Errors.Room.AlreadyMember;

        var membership = new RoomMembership
        {
            RoomId = roomId,
            UserId = _currentUser.UserId,
            Role = RoomRole.Member,
            JoinedAt = DateTime.UtcNow
        };

        _context.RoomMemberships.Add(membership);
        await _context.SaveChangesAsync();

        await _notifier.RoomMembershipChangedAsync(roomId, _currentUser.UserId, "joined");

        return Result.Success();
    }
}
