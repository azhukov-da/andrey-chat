using Application.Abstractions;
using Application.Common;
using Application.Features.Messages.Dtos;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Application.Services;

public class MessageService : IMessageService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IChatNotifier _notifier;

    public MessageService(IApplicationDbContext context, ICurrentUser currentUser, IChatNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<Result<MessageDto>> SendMessageAsync(Guid roomId, string text, Guid? replyToMessageId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        if (Encoding.UTF8.GetByteCount(text) > 3072)
            return Errors.Message.TooLarge;

        var membership = await _context.RoomMemberships
            .Include(m => m.Room)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId);

        if (membership == null)
            return Errors.Room.NotMember;

        if (membership.Room.DeletedAt != null)
            return Errors.Room.AlreadyDeleted;

        var isBanned = await _context.RoomBans
            .AnyAsync(b => b.RoomId == roomId && b.BannedUserId == _currentUser.UserId);

        if (isBanned)
            return Errors.Room.Banned;

        if (replyToMessageId.HasValue)
        {
            var replyToExists = await _context.Messages
                .AnyAsync(m => m.Id == replyToMessageId.Value && m.RoomId == roomId);

            if (!replyToExists)
                return Errors.Message.NotFound(replyToMessageId.Value);
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            AuthorId = _currentUser.UserId,
            Text = text,
            ReplyToMessageId = replyToMessageId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        var messageDto = new MessageDto
        {
            Id = message.Id,
            RoomId = message.RoomId,
            AuthorId = message.AuthorId,
            AuthorUserName = membership.User.UserName!,
            AuthorDisplayName = membership.User.DisplayName,
            Text = message.Text,
            ReplyToMessageId = message.ReplyToMessageId,
            EditedAt = message.EditedAt,
            IsDeleted = message.DeletedAt != null,
            CreatedAt = message.CreatedAt
        };

        await _notifier.MessageReceivedAsync(roomId, messageDto);

        return messageDto;
    }

    public async Task<Result<CursorPaged<MessageDto>>> GetMessageHistoryAsync(Guid roomId, Guid? before, int limit)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var isMember = await _context.RoomMemberships
            .AnyAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId);

        if (!isMember)
            return Errors.Room.NotMember;

        var query = _context.Messages
            .Where(m => m.RoomId == roomId);

        if (before.HasValue)
        {
            var beforeMessage = await _context.Messages
                .Where(m => m.Id == before.Value)
                .Select(m => new { m.CreatedAt, m.Id })
                .FirstOrDefaultAsync();

            if (beforeMessage != null)
            {
                query = query.Where(m =>
                    m.CreatedAt < beforeMessage.CreatedAt ||
                    (m.CreatedAt == beforeMessage.CreatedAt && m.Id.CompareTo(beforeMessage.Id) < 0));
            }
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit + 1)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                RoomId = m.RoomId,
                AuthorId = m.AuthorId,
                AuthorUserName = m.Author.UserName!,
                AuthorDisplayName = m.Author.DisplayName,
                Text = m.Text,
                ReplyToMessageId = m.ReplyToMessageId,
                EditedAt = m.EditedAt,
                IsDeleted = m.DeletedAt != null,
                CreatedAt = m.CreatedAt,
                Attachments = m.Attachments.Select(a => new AttachmentMetadataDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    SizeBytes = a.SizeBytes,
                    Kind = a.Kind.ToString(),
                    Comment = a.Comment
                }).ToList()
            })
            .ToListAsync();

        var hasMore = messages.Count > limit;
        if (hasMore)
            messages = messages.Take(limit).ToList();

        return new CursorPaged<MessageDto>
        {
            Items = messages,
            HasMore = hasMore,
            NextCursor = hasMore ? messages.Last().Id.ToString() : null
        };
    }

    public async Task<Result> EditMessageAsync(Guid messageId, string text)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        if (Encoding.UTF8.GetByteCount(text) > 3072)
            return Errors.Message.TooLarge;

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
            return Errors.Message.NotFound(messageId);

        if (message.AuthorId != _currentUser.UserId)
            return Errors.Message.NotAuthor;

        if (message.DeletedAt != null)
            return Errors.Message.AlreadyDeleted;

        message.Text = text;
        message.EditedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _notifier.MessageEditedAsync(message.RoomId, message.Id, message.Text, message.EditedAt.Value);

        return Result.Success();
    }

    public async Task<Result> DeleteMessageAsync(Guid messageId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var message = await _context.Messages
            .Include(m => m.Room)
            .ThenInclude(r => r.Memberships)
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
            return Errors.Message.NotFound(messageId);

        var isAuthor = message.AuthorId == _currentUser.UserId;
        var myMembership = message.Room.Memberships.FirstOrDefault(m => m.UserId == _currentUser.UserId);
        var canDelete = isAuthor || (myMembership != null && (myMembership.Role == RoomRole.Owner || myMembership.Role == RoomRole.Admin));

        if (!canDelete)
            return Errors.Message.NotAuthor;

        if (message.DeletedAt != null)
            return Errors.Message.AlreadyDeleted;

        message.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _notifier.MessageDeletedAsync(message.RoomId, message.Id);

        return Result.Success();
    }

    public async Task<Result> MarkReadAsync(Guid roomId, Guid messageId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var membership = await _context.RoomMemberships
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId);

        if (membership == null)
            return Errors.Room.NotMember;

        membership.LastReadMessageId = messageId;
        await _context.SaveChangesAsync();

        return Result.Success();
    }
}
