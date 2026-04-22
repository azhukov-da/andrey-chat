using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMembership> RoomMemberships => Set<RoomMembership>();
    public DbSet<RoomBan> RoomBans => Set<RoomBan>();
    public DbSet<RoomInvitation> RoomInvitations => Set<RoomInvitation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
        });

        builder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Description)
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.Name)
                .IsUnique()
                .HasFilter($"\"Kind\" = {(int)RoomKind.Group} AND \"DeletedAt\" IS NULL");

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<RoomMembership>(entity =>
        {
            entity.HasKey(e => new { e.RoomId, e.UserId });

            entity.HasOne(e => e.Room)
                .WithMany(r => r.Memberships)
                .HasForeignKey(e => e.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("NOW()");
        });

        builder.Entity<RoomBan>(entity =>
        {
            entity.HasKey(e => new { e.RoomId, e.BannedUserId });

            entity.HasOne(e => e.Room)
                .WithMany(r => r.Bans)
                .HasForeignKey(e => e.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.BannedUser)
                .WithMany()
                .HasForeignKey(e => e.BannedUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.BannedByUser)
                .WithMany()
                .HasForeignKey(e => e.BannedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Reason)
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
        });

        builder.Entity<RoomInvitation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Room)
                .WithMany(r => r.Invitations)
                .HasForeignKey(e => e.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.InvitedUser)
                .WithMany()
                .HasForeignKey(e => e.InvitedUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.InvitedByUser)
                .WithMany()
                .HasForeignKey(e => e.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
        });

        builder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Room)
                .WithMany(r => r.Messages)
                .HasForeignKey(e => e.RoomId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReplyToMessage)
                .WithMany()
                .HasForeignKey(e => e.ReplyToMessageId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Text)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(e => new { e.RoomId, e.CreatedAt, e.Id })
                .IsDescending(false, true, true);
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Message)
                .WithMany(m => m.Attachments)
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.StoragePath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.ContentType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Comment)
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
        });

        builder.Entity<Friendship>(entity =>
        {
            entity.HasKey(e => new { e.UserAId, e.UserBId });

            entity.HasOne(e => e.UserA)
                .WithMany()
                .HasForeignKey(e => e.UserAId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UserB)
                .WithMany()
                .HasForeignKey(e => e.UserBId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RequestedByUser)
                .WithMany()
                .HasForeignKey(e => e.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Message)
                .HasMaxLength(200);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");

            entity.ToTable(t => t.HasCheckConstraint("CK_Friendship_UserAId_LessThan_UserBId", "\"UserAId\" < \"UserBId\""));
        });

        builder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.DeviceInfo).HasMaxLength(200);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(64);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.LastSeenAt).HasDefaultValueSql("NOW()");

            entity.HasIndex(e => e.UserId);
        });

        builder.Entity<UserBlock>(entity =>
        {
            entity.HasKey(e => new { e.BlockerId, e.BlockedId });

            entity.HasOne(e => e.Blocker)
                .WithMany()
                .HasForeignKey(e => e.BlockerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Blocked)
                .WithMany()
                .HasForeignKey(e => e.BlockedId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("NOW()");
        });
    }
}

