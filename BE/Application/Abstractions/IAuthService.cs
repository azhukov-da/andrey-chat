using Application.Common;
using Application.Features.Auth.Dtos;

namespace Application.Abstractions;

public interface IAuthService
{
    Task<Result> LoginAsync(LoginRequestDto request);
    Task<Result> RegisterAsync(RegisterRequestDto request);
}
