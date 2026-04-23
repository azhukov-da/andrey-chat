using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.Common;
using Application.Features.Auth.Dtos;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

public class AuthService : IAuthService
{
    private static readonly Regex UsernameRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<Result> LoginAsync(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Errors.Auth.InvalidCredentials;

        var user = await _userManager.FindByEmailAsync(request.Email)
                   ?? await _userManager.FindByNameAsync(request.Email);
        if (user is null || user.UserName is null)
            return Errors.Auth.InvalidCredentials;

        _signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await _signInManager.PasswordSignInAsync(user.UserName, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
            return new Error("Auth.SignInFailed", result.ToString());

        return Result.Success();
    }

    public async Task<Result> RegisterAsync(RegisterRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Errors.Auth.EmailRequired;
        if (string.IsNullOrWhiteSpace(request.Username))
            return Errors.Auth.UsernameRequired;
        if (string.IsNullOrWhiteSpace(request.Password))
            return Errors.Auth.PasswordRequired;

        var username = request.Username.Trim();
        if (username.Length < 3 || username.Length > 32 || !UsernameRegex.IsMatch(username))
            return Errors.Auth.InvalidUsernameFormat;

        var email = request.Email.Trim();

        if (string.Equals(username, email, StringComparison.OrdinalIgnoreCase))
            return Errors.Auth.UsernameEqualsEmail;

        if (await _userManager.FindByNameAsync(username) != null)
            return Errors.Auth.UsernameTaken;

        if (await _userManager.FindByEmailAsync(email) != null)
            return Errors.Auth.EmailTaken;

        var user = new ApplicationUser
        {
            UserName = username,
            Email = email,
            CreatedAt = DateTime.UtcNow,
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return Errors.Auth.RegistrationFailed(createResult.Errors.Select(e => e.Description));

        return Result.Success();
    }
}
