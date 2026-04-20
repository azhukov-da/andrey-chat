using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DirectChatsController : ControllerBase
{
    private readonly IDirectChatService _directChatService;

    public DirectChatsController(IDirectChatService directChatService)
    {
        _directChatService = directChatService;
    }

    [HttpPost]
    public async Task<IActionResult> OpenOrCreate([FromBody] OpenDirectChatRequest request)
    {
        var result = await _directChatService.OpenOrCreateAsync(request.Username);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }
}

public record OpenDirectChatRequest(string Username);

