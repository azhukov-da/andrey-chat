using Application.Abstractions;
using Application.Features.Messages.Dtos;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/attachments")]
public class AttachmentsController : ControllerBase
{
    private const long MaxFileBytes = 20L * 1024 * 1024; // 20 MB
    private const long MaxImageBytes = 3L * 1024 * 1024; // 3 MB

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly IChatNotifier _notifier;

    public AttachmentsController(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IFileStorage storage,
        IChatNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _storage = storage;
        _notifier = notifier;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] Guid roomId,
        [FromForm] IFormFile file,
        [FromForm] string? comment,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "File is required." });

        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var kind = isImage ? AttachmentKind.Image : AttachmentKind.File;

        var maxBytes = isImage ? MaxImageBytes : MaxFileBytes;
        if (file.Length > maxBytes)
        {
            var error = isImage
                ? new { code = "Attachment.ImageTooLarge", message = "Image size exceeds maximum of 3 MB." }
                : new { code = "Attachment.FileTooLarge", message = "File size exceeds maximum of 20 MB." };
            return BadRequest(error);
        }

        // Verify membership in the room
        var membership = await _context.RoomMemberships
            .Include(m => m.Room)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId, ct);

        if (membership == null)
            return StatusCode(403, new { code = "Room.NotMember", message = "You are not a member of this room." });

        if (membership.Room.DeletedAt != null)
            return BadRequest(new { code = "Room.AlreadyDeleted", message = "Room has been deleted." });

        var isBanned = await _context.RoomBans
            .AnyAsync(b => b.RoomId == roomId && b.BannedUserId == _currentUser.UserId, ct);
        if (isBanned)
            return StatusCode(403, new { code = "Room.Banned", message = "You have been banned from this room." });

        // Create a message that holds this attachment so the file is bound to the chat history.
        var message = new Message
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            AuthorId = _currentUser.UserId,
            Text = string.IsNullOrWhiteSpace(comment) ? string.Empty : comment.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var attachmentId = Guid.NewGuid();

        string storagePath;
        await using (var stream = file.OpenReadStream())
        {
            storagePath = await _storage.SaveFileAsync(stream, file.FileName, contentType, roomId, attachmentId, ct);
        }

        var attachment = new Attachment
        {
            Id = attachmentId,
            MessageId = message.Id,
            FileName = file.FileName,
            StoragePath = storagePath,
            ContentType = contentType,
            SizeBytes = file.Length,
            Kind = kind,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);
        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync(ct);

        var dto = new MessageDto
        {
            Id = message.Id,
            RoomId = message.RoomId,
            AuthorId = message.AuthorId,
            AuthorUserName = membership.User.UserName!,
            AuthorDisplayName = membership.User.DisplayName,
            Text = message.Text,
            ReplyToMessageId = null,
            EditedAt = null,
            IsDeleted = false,
            CreatedAt = message.CreatedAt,
            Attachments = new List<AttachmentMetadataDto>
            {
                new AttachmentMetadataDto
                {
                    Id = attachment.Id,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeBytes = attachment.SizeBytes,
                    Kind = attachment.Kind.ToString(),
                    Comment = attachment.Comment
                }
            }
        };

        await _notifier.MessageReceivedAsync(roomId, dto, ct);

        return Ok(dto);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Unauthorized();

        var attachment = await _context.Attachments
            .Include(a => a.Message)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (attachment == null)
            return NotFound();

        var roomId = attachment.Message.RoomId;

        var isMember = await _context.RoomMemberships
            .AnyAsync(m => m.RoomId == roomId && m.UserId == _currentUser.UserId, ct);
        if (!isMember)
            return StatusCode(403, new { code = "Room.NotMember", message = "You are not a member of this room." });

        if (!_storage.FileExists(attachment.StoragePath))
            return NotFound();

        var stream = await _storage.GetFileAsync(attachment.StoragePath, ct);
        return File(stream, attachment.ContentType, attachment.FileName);
    }
}
