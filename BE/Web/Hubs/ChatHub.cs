using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Web.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly IPresenceTracker _presenceTracker;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ChatHub(
        IMessageService messageService,
        IPresenceTracker presenceTracker,
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _messageService = messageService;
        _presenceTracker = presenceTracker;
        _context = context;
        _currentUser = currentUser;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
        {
            await _presenceTracker.UserConnectedAsync(Context.ConnectionId, userId);

            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

            var userRooms = await _context.RoomMemberships
                .Where(m => m.UserId == userId)
                .Select(m => m.RoomId)
                .ToListAsync();

            foreach (var roomId in userRooms)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"room:{roomId}");
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _presenceTracker.UserDisconnectedAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Ping(bool active)
    {
        await _presenceTracker.UpdateActivityAsync(Context.ConnectionId, active);
    }

    public async Task SendMessage(Guid roomId, string text, Guid? replyToMessageId)
    {
        var result = await _messageService.SendMessageAsync(roomId, text, replyToMessageId);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error?.Message ?? "Failed to send message");
        }
    }

    public async Task EditMessage(Guid messageId, string text)
    {
        var result = await _messageService.EditMessageAsync(messageId, text);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error?.Message ?? "Failed to edit message");
        }
    }

    public async Task DeleteMessage(Guid messageId)
    {
        var result = await _messageService.DeleteMessageAsync(messageId);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error?.Message ?? "Failed to delete message");
        }
    }

    public async Task MarkRead(Guid roomId, Guid messageId)
    {
        var result = await _messageService.MarkReadAsync(roomId, messageId);

        if (!result.IsSuccess)
        {
            throw new HubException(result.Error?.Message ?? "Failed to mark as read");
        }
    }

    public async Task StartTyping(Guid roomId)
    {
        await Clients.OthersInGroup($"room:{roomId}")
            .SendAsync("UserTyping", new { userId = _currentUser.UserId, roomId });
    }

    public async Task StopTyping(Guid roomId)
    {
        await Clients.OthersInGroup($"room:{roomId}")
            .SendAsync("UserStoppedTyping", new { userId = _currentUser.UserId, roomId });
    }
}

