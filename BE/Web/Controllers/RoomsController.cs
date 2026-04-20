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
}

public record CreateRoomRequest(string Name, string? Description, RoomVisibility Visibility);

