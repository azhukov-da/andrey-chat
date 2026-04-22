using Application.Abstractions;
using Application.Common;
using Application.Features.Sessions.Dtos;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class SessionService : ISessionService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public SessionService(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<SessionRegistrationDto>> RegisterAsync(string? deviceInfo, string? userAgent, string? ipAddress)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            DeviceInfo = Truncate(deviceInfo, 200),
            UserAgent = Truncate(userAgent, 500),
            IpAddress = Truncate(ipAddress, 64),
            CreatedAt = now,
            LastSeenAt = now,
        };

        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync();

        return new SessionRegistrationDto(session.Id);
    }

    public async Task<Result<IReadOnlyList<UserSessionDto>>> ListAsync(Guid? currentSessionId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var sessions = await _context.UserSessions
            .Where(s => s.UserId == _currentUser.UserId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new UserSessionDto
            {
                Id = s.Id,
                DeviceInfo = s.DeviceInfo,
                UserAgent = s.UserAgent,
                IpAddress = s.IpAddress,
                CreatedAt = s.CreatedAt,
                LastSeenAt = s.LastSeenAt,
                IsCurrent = currentSessionId.HasValue && s.Id == currentSessionId.Value,
            })
            .ToListAsync();

        return Result<IReadOnlyList<UserSessionDto>>.Success(sessions);
    }

    public async Task<Result> RevokeAsync(Guid sessionId)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == _currentUser.UserId);

        if (session == null)
            return new Error("Session.NotFound", "Session not found.");

        session.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Result.Success();
    }

    public async Task TouchAsync(Guid sessionId)
    {
        var session = await _context.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null || session.RevokedAt != null) return;
        session.LastSeenAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
}
