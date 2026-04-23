using Application.Abstractions;
using Application.Features.Auth.Dtos;
using Microsoft.AspNetCore.Mvc;
using Web.Models;

namespace Web.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { title = "Email and password are required." });

        var result = await _authService.LoginAsync(new LoginRequestDto(request.Email, request.Password));
        if (!result.IsSuccess)
            return Problem(detail: result.Error!.Message, statusCode: StatusCodes.Status401Unauthorized);

        return new EmptyResult();
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request)
    {
        if (request is null)
            return BadRequest(new { errors = new Dictionary<string, string[]> { ["request"] = new[] { "Request body is required." } } });

        var result = await _authService.RegisterAsync(new RegisterRequestDto(request.Email, request.Username, request.Password));
        if (!result.IsSuccess)
            return BadRequest(result.Error!.ToValidationResponse());

        return Ok();
    }
}
