using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Models;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;

    public SessionsController(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    private Guid? CurrentSessionId
    {
        get
        {
            if (Request.Headers.TryGetValue("X-Session-Id", out var v) && Guid.TryParse(v.ToString(), out var id))
                return id;
            return null;
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterSessionRequest? request)
    {
        var ua = Request.Headers["User-Agent"].ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _sessionService.RegisterAsync(request?.DeviceInfo, ua, ip);
        if (!result.IsSuccess) return BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var result = await _sessionService.ListAsync(CurrentSessionId);
        if (!result.IsSuccess) return BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpDelete("current")]
    public async Task<IActionResult> RevokeCurrent()
    {
        var id = CurrentSessionId;
        if (id == null) return BadRequest(new { error = "No current session header." });
        var result = await _sessionService.RevokeAsync(id.Value);
        if (!result.IsSuccess) return BadRequest(result.Error);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var result = await _sessionService.RevokeAsync(id);
        if (!result.IsSuccess) return BadRequest(result.Error);
        return NoContent();
    }
}

