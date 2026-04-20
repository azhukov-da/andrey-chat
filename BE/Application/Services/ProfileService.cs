using Application.Abstractions;
using Application.Common;
using Application.Features.Profile.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class ProfileService : IProfileService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ProfileService(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<UserProfileDto>> GetMeAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var user = await _context.Users
            .Where(u => u.Id == _currentUser.UserId)
            .Select(u => new UserProfileDto
            {
                Id = u.Id,
                UserName = u.UserName!,
                DisplayName = u.DisplayName,
                Email = u.Email,
                CreatedAt = u.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return Errors.User.NotFound(_currentUser.UserId);

        return user;
    }

    public async Task<Result> UpdateDisplayNameAsync(string displayName)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _currentUser.UserId);

        if (user == null)
            return Errors.User.NotFound(_currentUser.UserId);

        user.DisplayName = displayName;
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteAccountAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Errors.Authorization.Unauthorized;

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == _currentUser.UserId);

        if (user == null)
            return Errors.User.NotFound(_currentUser.UserId);

        if (user.DeletedAt != null)
            return Errors.User.AlreadyDeleted;

        user.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Result.Success();
    }
}
