using Application.Abstractions;
using System.Collections.Concurrent;

namespace Infrastructure.Services;

public class ConnectionInfo
{
    public string UserId { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; }
    public bool IsActive { get; set; }
}

public class InMemoryPresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
    private readonly IChatNotifier _notifier;
    private readonly TimeSpan _onlineThreshold = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _afkThreshold = TimeSpan.FromSeconds(60);

    public InMemoryPresenceTracker(IChatNotifier notifier)
    {
        _notifier = notifier;
    }

    public Task UserConnectedAsync(string connectionId, string userId)
    {
        _connections[connectionId] = new ConnectionInfo
        {
            UserId = userId,
            LastActivity = DateTime.UtcNow,
            IsActive = true
        };

        return NotifyPresenceChangedAsync(userId);
    }

    public async Task UserDisconnectedAsync(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            await NotifyPresenceChangedAsync(connection.UserId);
        }
    }

    public Task UpdateActivityAsync(string connectionId, bool isActive)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            connection.LastActivity = DateTime.UtcNow;
            connection.IsActive = isActive;

            return NotifyPresenceChangedAsync(connection.UserId);
        }

        return Task.CompletedTask;
    }

    public Task<PresenceStatus> GetUserStatusAsync(string userId)
    {
        var userConnections = _connections.Values.Where(c => c.UserId == userId).ToList();

        if (!userConnections.Any())
            return Task.FromResult(PresenceStatus.Offline);

        var now = DateTime.UtcNow;
        var hasActiveConnection = userConnections.Any(c =>
            c.IsActive && (now - c.LastActivity) < _onlineThreshold);

        if (hasActiveConnection)
            return Task.FromResult(PresenceStatus.Online);

        var hasRecentConnection = userConnections.Any(c =>
            (now - c.LastActivity) < _afkThreshold);

        return Task.FromResult(hasRecentConnection ? PresenceStatus.Away : PresenceStatus.Offline);
    }

    public async Task<Dictionary<string, PresenceStatus>> GetUsersStatusAsync(IEnumerable<string> userIds)
    {
        var result = new Dictionary<string, PresenceStatus>();

        foreach (var userId in userIds)
        {
            result[userId] = await GetUserStatusAsync(userId);
        }

        return result;
    }

    private async Task NotifyPresenceChangedAsync(string userId)
    {
        var status = await GetUserStatusAsync(userId);
        await _notifier.PresenceChangedAsync(userId, status);
    }
}
