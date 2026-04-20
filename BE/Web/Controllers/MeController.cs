using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MeController : ControllerBase
{
    private readonly IProfileService _profileService;

    public MeController(IProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMe()
    {
        var result = await _profileService.GetMeAsync();
        
        if (!result.IsSuccess)
            return NotFound(result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("display-name")]
    public async Task<IActionResult> UpdateDisplayName([FromBody] UpdateDisplayNameRequest request)
    {
        var result = await _profileService.UpdateDisplayNameAsync(request.DisplayName);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount()
    {
        var result = await _profileService.DeleteAccountAsync();
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }
}

public record UpdateDisplayNameRequest(string DisplayName);

