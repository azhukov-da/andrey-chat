using Application.Common;
using Application.Features.Profile.Dtos;

namespace Application.Abstractions;

public interface IProfileService
{
    Task<Result<UserProfileDto>> GetMeAsync();
    Task<Result> UpdateDisplayNameAsync(string displayName);
    Task<Result> DeleteAccountAsync();
}
