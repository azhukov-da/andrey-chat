using Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Web.Hubs;

public class ChatNotifier : IChatNotifier
{
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatNotifier(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task MessageReceivedAsync(Guid roomId, object messageDto, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"room:{roomId}")
            .SendAsync("MessageReceived", messageDto, cancellationToken);
    }

    public async Task MessageEditedAsync(Guid roomId, Guid messageId, string newText, DateTime editedAt, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"room:{roomId}")
            .SendAsync("MessageEdited", new { messageId, newText, editedAt }, cancellationToken);
    }

    public async Task MessageDeletedAsync(Guid roomId, Guid messageId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"room:{roomId}")
            .SendAsync("MessageDeleted", new { messageId }, cancellationToken);
    }

    public async Task PresenceChangedAsync(string userId, PresenceStatus status, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.All
            .SendAsync("PresenceChanged", new { userId, status = status.ToString() }, cancellationToken);
    }

    public async Task RoomMembershipChangedAsync(Guid roomId, string userId, string action, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"room:{roomId}")
            .SendAsync("RoomMembershipChanged", new { roomId, userId, action }, cancellationToken);
    }

    public async Task UnreadUpdatedAsync(string userId, Guid roomId, int unreadCount, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"user:{userId}")
            .SendAsync("UnreadUpdated", new { roomId, unreadCount }, cancellationToken);
    }

    public async Task FriendRequestReceivedAsync(string userId, object friendRequestDto, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"user:{userId}")
            .SendAsync("FriendRequestReceived", friendRequestDto, cancellationToken);
    }

    public async Task RoomDeletedAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"room:{roomId}")
            .SendAsync("RoomDeleted", new { roomId }, cancellationToken);
    }
}
