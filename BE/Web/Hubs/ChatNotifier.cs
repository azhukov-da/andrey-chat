using Application.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Web.Hubs;

public class ChatNotifier : IChatNotifier
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IPresenceTracker _presenceTracker;
    private readonly IServiceProvider _serviceProvider;

    public ChatNotifier(IHubContext<ChatHub> hubContext, IPresenceTracker presenceTracker, IServiceProvider serviceProvider)
    {
        _hubContext = hubContext;
        _presenceTracker = presenceTracker;
        _serviceProvider = serviceProvider;
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

    public async Task AddUserToRoomGroupAsync(string userId, Guid roomId, CancellationToken cancellationToken = default)
    {
        var connectionIds = await _presenceTracker.GetConnectionIdsAsync(userId);
        foreach (var connectionId in connectionIds)
        {
            await _hubContext.Groups.AddToGroupAsync(connectionId, $"room:{roomId}", cancellationToken);
        }
    }

    public async Task EnrollUserGroupsAsync(string connectionId, string userId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Groups.AddToGroupAsync(connectionId, $"user:{userId}", cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var roomIds = await context.RoomMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.RoomId)
            .ToListAsync(cancellationToken);

        foreach (var roomId in roomIds)
        {
            await _hubContext.Groups.AddToGroupAsync(connectionId, $"room:{roomId}", cancellationToken);
        }
    }
}
