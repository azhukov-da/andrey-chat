using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[Authorize]
[ApiController]
public class InvitationsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public InvitationsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet("api/me/invitations")]
    public async Task<IActionResult> ListMine()
    {
        var result = await _roomService.ListMyInvitationsAsync();
        if (!result.IsSuccess) return MapError(result.Error);
        return Ok(result.Value);
    }

    [HttpPost("api/invitations/{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var result = await _roomService.AcceptInvitationAsync(id);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    [HttpPost("api/invitations/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
    {
        var result = await _roomService.RejectInvitationAsync(id);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    private IActionResult MapError(Application.Common.Error? error)
    {
        if (error == null) return BadRequest();
        if (error.Code == "Invitation.NotFound" || error.Code == "Room.NotFound") return NotFound(error);
        if (error.Code == "Authorization.Unauthorized") return Unauthorized(error);
        if (error.Code == "Invitation.NotRecipient" || error.Code == "Authorization.Forbidden")
            return StatusCode(403, error);
        return BadRequest(error);
    }
}
