namespace Application.Abstractions;

public interface IChatNotifier
{
    Task MessageReceivedAsync(Guid roomId, object messageDto, CancellationToken cancellationToken = default);
    Task MessageEditedAsync(Guid roomId, Guid messageId, string newText, DateTime editedAt, CancellationToken cancellationToken = default);
    Task MessageDeletedAsync(Guid roomId, Guid messageId, CancellationToken cancellationToken = default);
    Task PresenceChangedAsync(string userId, PresenceStatus status, CancellationToken cancellationToken = default);
    Task RoomMembershipChangedAsync(Guid roomId, string userId, string action, CancellationToken cancellationToken = default);
    Task UnreadUpdatedAsync(string userId, Guid roomId, int unreadCount, CancellationToken cancellationToken = default);
    Task FriendRequestReceivedAsync(string userId, object friendRequestDto, CancellationToken cancellationToken = default);
    Task RoomDeletedAsync(Guid roomId, CancellationToken cancellationToken = default);
}
