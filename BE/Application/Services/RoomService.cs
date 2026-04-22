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

        await _notifier.AddUserToRoomGroupAsync(_currentUser.UserId, roomId);
        await _notifier.RoomMembershipChangedAsync(roomId, _currentUser.UserId, "joined");

        return Result.Success();
    }

    public async Task<Result<RoomDto>> UpdateRoomAsync(Guid roomId, string name, string? description, RoomVisibility visibility)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        if (string.IsNullOrWhiteSpace(name))
            return new Error("Room.NameRequired", "Room name is required.");

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.DeletedAt == null);
        if (room == null)
            return Errors.Room.NotFound(roomId);

        if (room.OwnerId != _currentUser.UserId)
            return Errors.Room.NotOwner;

        var trimmedName = name.Trim();
        if (!string.Equals(trimmedName, room.Name, StringComparison.Ordinal))
        {
            var nameExists = await _context.Rooms
                .AnyAsync(r => r.Id != roomId && r.Name == trimmedName && r.Kind == RoomKind.Group && r.DeletedAt == null);
            if (nameExists)
                return Errors.Room.NameAlreadyExists;
        }

        room.Name = trimmedName;
        room.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        room.Visibility = visibility;

        await _context.SaveChangesAsync();

        var memberCount = await _context.RoomMemberships.CountAsync(m => m.RoomId == roomId);
        var ownerUserName = await _context.Users.Where(u => u.Id == room.OwnerId).Select(u => u.UserName).FirstOrDefaultAsync();

        return new RoomDto
        {
            Id = room.Id,
            Name = room.Name,
            Description = room.Description,
            Visibility = room.Visibility,
            Kind = room.Kind,
            OwnerId = room.OwnerId,
            OwnerUserName = ownerUserName,
            IsFrozen = room.IsFrozen,
            CreatedAt = room.CreatedAt,
            MemberCount = memberCount,
            MyRole = RoomRole.Owner
        };
    }

    public async Task<Result> DeleteRoomAsync(Guid roomId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var room = await _context.Rooms
            .FirstOrDefaultAsync(r => r.Id == roomId);

        if (room == null || room.DeletedAt != null)
            return Errors.Room.NotFound(roomId);

        if (room.OwnerId != _currentUser.UserId)
            return Errors.Room.NotOwner;

        room.DeletedAt = DateTime.UtcNow;

        // Cascade: soft-delete all messages in the room (hard-delete attachments file content).
        var messages = await _context.Messages
            .Where(m => m.RoomId == roomId && m.DeletedAt == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var message in messages)
        {
            message.DeletedAt = now;
        }

        await _context.SaveChangesAsync();

        await _notifier.RoomDeletedAsync(roomId);

        return Result.Success();
    }

    public async Task<Result<List<RoomMemberDto>>> ListMembersAsync(Guid roomId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.DeletedAt == null);
        if (room == null)
            return Errors.Room.NotFound(roomId);

        var isMember = await _context.RoomMemberships
            .AnyAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId);
        if (!isMember)
            return Errors.Room.NotMember;

        var members = await _context.RoomMemberships
            .Where(m => m.RoomId == roomId)
            .Select(m => new RoomMemberDto
            {
                UserId = m.UserId,
                UserName = m.User.UserName ?? string.Empty,
                DisplayName = m.User.DisplayName,
                Role = m.Role,
                JoinedAt = m.JoinedAt
            })
            .ToListAsync();

        return members;
    }

    private async Task<(Result? error, RoomMembership? callerMembership, Room? room)> RequireAdminAsync(Guid roomId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return (Errors.Authorization.Unauthorized, null, null);

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.DeletedAt == null);
        if (room == null)
            return (Errors.Room.NotFound(roomId), null, null);

        var caller = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId);

        if (caller == null || (caller.Role != RoomRole.Admin && caller.Role != RoomRole.Owner))
            return (Errors.Room.NotOwnerOrAdmin, null, null);

        return (null, caller, room);
    }

    public async Task<Result> MakeAdminAsync(Guid roomId, string targetUserId)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error;

        var target = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetUserId);
        if (target == null) return Errors.Room.NotMember;

        if (target.Role == RoomRole.Owner) return Errors.Room.NotOwnerOrAdmin;
        if (target.Role == RoomRole.Admin) return Result.Success();

        target.Role = RoomRole.Admin;
        await _context.SaveChangesAsync();
        await _notifier.RoomMembershipChangedAsync(roomId, targetUserId, "promoted");
        return Result.Success();
    }

    public async Task<Result> RemoveAdminAsync(Guid roomId, string targetUserId)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error;

        var target = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetUserId);
        if (target == null) return Errors.Room.NotMember;

        // Owner cannot lose admin
        if (target.Role == RoomRole.Owner) return Errors.Room.NotOwner;

        // Only owner may remove another admin (per requirement: admins may remove other admins except the owner; owner may remove any admin)
        // We'll allow admins to remove other admins too (requirement 2.4.7 explicitly permits that).
        if (target.Role != RoomRole.Admin) return Result.Success();

        target.Role = RoomRole.Member;
        await _context.SaveChangesAsync();
        await _notifier.RoomMembershipChangedAsync(roomId, targetUserId, "demoted");
        return Result.Success();
    }

    public async Task<Result> RemoveMemberAsync(Guid roomId, string targetUserId)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error;

        var target = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetUserId);
        if (target == null) return Errors.Room.NotMember;

        if (target.Role == RoomRole.Owner) return Errors.Room.NotOwner;

        // Admins cannot remove other admins unless caller is Owner
        if (target.Role == RoomRole.Admin && caller!.Role != RoomRole.Owner)
            return Errors.Authorization.Forbidden;

        _context.RoomMemberships.Remove(target);
        await _context.SaveChangesAsync();

        await _notifier.RoomMembershipChangedAsync(roomId, targetUserId, "removed");
        return Result.Success();
    }

    public async Task<Result> BanMemberAsync(Guid roomId, string targetUserId, string? reason)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error;

        if (targetUserId == room!.OwnerId) return Errors.Room.NotOwner;

        var target = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetUserId);

        if (target != null)
        {
            if (target.Role == RoomRole.Owner) return Errors.Room.NotOwner;
            if (target.Role == RoomRole.Admin && caller!.Role != RoomRole.Owner)
                return Errors.Authorization.Forbidden;

            _context.RoomMemberships.Remove(target);
        }

        var existingBan = await _context.RoomBans
            .FirstOrDefaultAsync(b => b.RoomId == roomId && b.BannedUserId == targetUserId);
        if (existingBan == null)
        {
            _context.RoomBans.Add(new RoomBan
            {
                RoomId = roomId,
                BannedUserId = targetUserId,
                BannedByUserId = _currentUser.UserId!,
                Reason = reason,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        await _notifier.RoomMembershipChangedAsync(roomId, targetUserId, "banned");
        return Result.Success();
    }

    public async Task<Result> UnbanMemberAsync(Guid roomId, string targetUserId)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error;

        var ban = await _context.RoomBans
            .FirstOrDefaultAsync(b => b.RoomId == roomId && b.BannedUserId == targetUserId);
        if (ban == null) return Result.Success();

        _context.RoomBans.Remove(ban);
        await _context.SaveChangesAsync();
        await _notifier.RoomMembershipChangedAsync(roomId, targetUserId, "unbanned");
        return Result.Success();
    }

    public async Task<Result> LeaveRoomAsync(Guid roomId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.DeletedAt == null);
        if (room == null)
            return Errors.Room.NotFound(roomId);

        if (room.OwnerId == _currentUser.UserId)
            return new Error("Room.OwnerCannotLeave", "Room owner cannot leave the room. Delete the room instead.");

        var membership = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId);
        if (membership == null)
            return Errors.Room.NotMember;

        _context.RoomMemberships.Remove(membership);
        await _context.SaveChangesAsync();

        await _notifier.RoomMembershipChangedAsync(roomId, _currentUser.UserId, "left");
        return Result.Success();
    }

    public async Task<Result<RoomInvitationDto>> InviteUserAsync(Guid roomId, string inviteeUsername)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error.Error!;

        if (string.IsNullOrWhiteSpace(inviteeUsername))
            return new Error("Invitation.UsernameRequired", "Invitee username is required.");

        var invitee = await _context.Users.FirstOrDefaultAsync(u => u.UserName == inviteeUsername);
        if (invitee == null)
            return Errors.User.NotFoundByUsername(inviteeUsername);

        if (invitee.Id == _currentUser.UserId)
            return new Error("Invitation.CannotInviteSelf", "You cannot invite yourself.");

        var alreadyMember = await _context.RoomMemberships
            .AnyAsync(m => m.RoomId == roomId && m.UserId == invitee.Id);
        if (alreadyMember)
            return Errors.Room.AlreadyMember;

        var isBanned = await _context.RoomBans
            .AnyAsync(b => b.RoomId == roomId && b.BannedUserId == invitee.Id);
        if (isBanned)
            return Errors.Room.Banned;

        var existing = await _context.RoomInvitations
            .FirstOrDefaultAsync(i => i.RoomId == roomId && i.InvitedUserId == invitee.Id && i.Status == InvitationStatus.Pending);
        if (existing != null)
            return new Error("Invitation.AlreadyExists", "A pending invitation already exists for this user.");

        var invitation = new RoomInvitation
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            InvitedUserId = invitee.Id,
            InvitedByUserId = _currentUser.UserId!,
            Status = InvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.RoomInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        return new RoomInvitationDto
        {
            Id = invitation.Id,
            RoomId = roomId,
            RoomName = room!.Name,
            InvitedUserId = invitee.Id,
            InvitedUserName = invitee.UserName ?? string.Empty,
            InvitedByUserId = _currentUser.UserId!,
            InvitedByUserName = _currentUser.UserName ?? string.Empty,
            Status = invitation.Status,
            CreatedAt = invitation.CreatedAt
        };
    }

    public async Task<Result<List<RoomInvitationDto>>> ListMyInvitationsAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var invitations = await _context.RoomInvitations
            .Where(i => i.InvitedUserId == _currentUser.UserId && i.Status == InvitationStatus.Pending && i.Room.DeletedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new RoomInvitationDto
            {
                Id = i.Id,
                RoomId = i.RoomId,
                RoomName = i.Room.Name,
                InvitedUserId = i.InvitedUserId,
                InvitedUserName = i.InvitedUser.UserName ?? string.Empty,
                InvitedByUserId = i.InvitedByUserId,
                InvitedByUserName = i.InvitedByUser.UserName ?? string.Empty,
                Status = i.Status,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return invitations;
    }

    public async Task<Result> AcceptInvitationAsync(Guid invitationId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var invitation = await _context.RoomInvitations
            .Include(i => i.Room)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null) return Errors.Invitation.NotFound;
        if (invitation.InvitedUserId != _currentUser.UserId) return Errors.Invitation.NotRecipient;
        if (invitation.Status != InvitationStatus.Pending) return Errors.Invitation.AlreadyProcessed;
        if (invitation.Room.DeletedAt != null) return Errors.Room.NotFound(invitation.RoomId);

        var isBanned = await _context.RoomBans
            .AnyAsync(b => b.RoomId == invitation.RoomId && b.BannedUserId == _currentUser.UserId);
        if (isBanned) return Errors.Room.Banned;

        invitation.Status = InvitationStatus.Accepted;

        var alreadyMember = await _context.RoomMemberships
            .AnyAsync(m => m.RoomId == invitation.RoomId && m.UserId == _currentUser.UserId);

        if (!alreadyMember)
        {
            _context.RoomMemberships.Add(new RoomMembership
            {
                RoomId = invitation.RoomId,
                UserId = _currentUser.UserId!,
                Role = RoomRole.Member,
                JoinedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        if (!alreadyMember)
        {
            await _notifier.AddUserToRoomGroupAsync(_currentUser.UserId!, invitation.RoomId);
            await _notifier.RoomMembershipChangedAsync(invitation.RoomId, _currentUser.UserId!, "joined");
        }

        return Result.Success();
    }

    public async Task<Result> RejectInvitationAsync(Guid invitationId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var invitation = await _context.RoomInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null) return Errors.Invitation.NotFound;
        if (invitation.InvitedUserId != _currentUser.UserId) return Errors.Invitation.NotRecipient;
        if (invitation.Status != InvitationStatus.Pending) return Errors.Invitation.AlreadyProcessed;

        invitation.Status = InvitationStatus.Rejected;
        await _context.SaveChangesAsync();
        return Result.Success();
    }

    public async Task<Result<List<RoomBanDto>>> ListBannedAsync(Guid roomId)
    {
        var (error, caller, room) = await RequireAdminAsync(roomId);
        if (error != null) return error.Error!;

        var bans = await _context.RoomBans
            .Where(b => b.RoomId == roomId)
            .Select(b => new RoomBanDto
            {
                BannedUserId = b.BannedUserId,
                BannedUserName = b.BannedUser.UserName ?? string.Empty,
                BannedByUserId = b.BannedByUserId,
                BannedByUserName = b.BannedByUser.UserName ?? string.Empty,
                Reason = b.Reason,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();

        return bans;
    }
}
