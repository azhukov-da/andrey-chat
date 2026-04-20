using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<Room> Rooms { get; }
    DbSet<RoomMembership> RoomMemberships { get; }
    DbSet<RoomBan> RoomBans { get; }
    DbSet<RoomInvitation> RoomInvitations { get; }
    DbSet<Message> Messages { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Friendship> Friendships { get; }
    DbSet<UserBlock> UserBlocks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
