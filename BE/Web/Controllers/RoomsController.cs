using Application.Abstractions;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet("public")]
    [AllowAnonymous]
    public async Task<IActionResult> ListPublic([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _roomService.ListPublicRoomsAsync(search, page, pageSize);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }

    [HttpGet("mine")]
    public async Task<IActionResult> ListMine()
    {
        var result = await _roomService.ListMyRoomsAsync();
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var result = await _roomService.GetRoomAsync(id);
        
        if (!result.IsSuccess)
            return NotFound(result.Error);

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoomRequest request)
    {
        var result = await _roomService.CreateRoomAsync(request.Name, request.Description, request.Visibility);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPost("{id:guid}/join")]
    public async Task<IActionResult> Join(Guid id)
    {
        var result = await _roomService.JoinRoomAsync(id);

        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _roomService.DeleteRoomAsync(id);

        if (!result.IsSuccess)
        {
            if (result.Error?.Code == "Room.NotFound")
                return NotFound(result.Error);
            if (result.Error?.Code == "Room.NotOwner" || result.Error?.Code == "Authorization.Unauthorized")
                return Forbid();
            return BadRequest(result.Error);
        }

        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> ListMembers(Guid id)
    {
        var result = await _roomService.ListMembersAsync(id);
        if (!result.IsSuccess) return MapError(result.Error);
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/members/{userId}/make-admin")]
    public async Task<IActionResult> MakeAdmin(Guid id, string userId)
    {
        var result = await _roomService.MakeAdminAsync(id, userId);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    [HttpPost("{id:guid}/members/{userId}/remove-admin")]
    public async Task<IActionResult> RemoveAdmin(Guid id, string userId)
    {
        var result = await _roomService.RemoveAdminAsync(id, userId);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(Guid id, string userId)
    {
        var result = await _roomService.RemoveMemberAsync(id, userId);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    [HttpPost("{id:guid}/bans/{userId}")]
    public async Task<IActionResult> BanMember(Guid id, string userId, [FromBody] BanMemberRequest? request)
    {
        var result = await _roomService.BanMemberAsync(id, userId, request?.Reason);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    [HttpDelete("{id:guid}/bans/{userId}")]
    public async Task<IActionResult> UnbanMember(Guid id, string userId)
    {
        var result = await _roomService.UnbanMemberAsync(id, userId);
        if (!result.IsSuccess) return MapError(result.Error);
        return NoContent();
    }

    [HttpGet("{id:guid}/bans")]
    public async Task<IActionResult> ListBanned(Guid id)
    {
        var result = await _roomService.ListBannedAsync(id);
        if (!result.IsSuccess) return MapError(result.Error);
        return Ok(result.Value);
    }

    private IActionResult MapError(Application.Common.Error? error)
    {
        if (error == null) return BadRequest();
        if (error.Code == "Room.NotFound") return NotFound(error);
        if (error.Code == "Authorization.Unauthorized") return Unauthorized(error);
        if (error.Code == "Authorization.Forbidden" || error.Code == "Room.NotOwner" || error.Code == "Room.NotOwnerOrAdmin")
            return StatusCode(403, error);
        return BadRequest(error);
    }
}

public record CreateRoomRequest(string Name, string? Description, RoomVisibility Visibility);
public record BanMemberRequest(string? Reason);

