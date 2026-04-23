using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Models;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FriendsController : ControllerBase
{
    private readonly IFriendService _friendService;

    public FriendsController(IFriendService friendService)
    {
        _friendService = friendService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var result = await _friendService.ListFriendsAsync();
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }

    [HttpPost("requests")]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestRequest request)
    {
        var result = await _friendService.SendFriendRequestAsync(request.Username, request.Message);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpPost("requests/{userId}/accept")]
    public async Task<IActionResult> AcceptRequest(string userId)
    {
        var result = await _friendService.AcceptFriendRequestAsync(userId);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpPost("requests/{userId}/reject")]
    public async Task<IActionResult> RejectRequest(string userId)
    {
        var result = await _friendService.RejectFriendRequestAsync(userId);

        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpDelete("{userId}")]
    public async Task<IActionResult> Remove(string userId)
    {
        var result = await _friendService.RemoveFriendAsync(userId);

        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpPost("blocks/{userId}")]
    public async Task<IActionResult> Block(string userId)
    {
        var result = await _friendService.BlockUserAsync(userId);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpDelete("blocks/{userId}")]
    public async Task<IActionResult> Unblock(string userId)
    {
        var result = await _friendService.UnblockUserAsync(userId);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }
}


