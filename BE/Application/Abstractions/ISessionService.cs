using Application.Common;
using Application.Features.Sessions.Dtos;

namespace Application.Abstractions;

public interface ISessionService
{
    Task<Result<SessionRegistrationDto>> RegisterAsync(string? deviceInfo, string? userAgent, string? ipAddress);
    Task<Result<IReadOnlyList<UserSessionDto>>> ListAsync(Guid? currentSessionId);
    Task<Result> RevokeAsync(Guid sessionId);
    Task TouchAsync(Guid sessionId);
}

public record SessionRegistrationDto(Guid Id);
