namespace Application.Abstractions;

public enum PresenceStatus
{
    Offline,
    Online,
    Away
}

public interface IPresenceTracker
{
    Task UserConnectedAsync(string connectionId, string userId);
    Task UserDisconnectedAsync(string connectionId);
    Task UpdateActivityAsync(string connectionId, bool isActive);
    Task<PresenceStatus> GetUserStatusAsync(string userId);
    Task<Dictionary<string, PresenceStatus>> GetUsersStatusAsync(IEnumerable<string> userIds);
}
