using Application.Abstractions;
using Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/transcribe")]
public class TranscriptionController : ControllerBase
{
    private static readonly TimeSpan MaxDictationDuration = TimeSpan.FromSeconds(120);

    private readonly ITranscriptionService _transcriptionService;
    private readonly ICurrentUser _currentUser;

    public TranscriptionController(ITranscriptionService transcriptionService, ICurrentUser currentUser)
    {
        _transcriptionService = transcriptionService;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Transcribe([FromForm] IFormFile file, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
            return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(Errors.Transcription.EmptyOrUndecodable.ToValidationResponse());

        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        await using var stream = file.OpenReadStream();
        var result = await _transcriptionService.TranscribeAsync(stream, file.FileName, contentType, MaxDictationDuration, ct);

        if (!result.IsSuccess)
        {
            var error = result.Error!;
            if (error.Code == Errors.Transcription.EmptyOrUndecodable.Code || error.Code == "Transcription.TooLong")
                return BadRequest(error.ToValidationResponse());

            return StatusCode(503, error.ToValidationResponse());
        }

        return Ok(new { text = result.Value!.Text, language = result.Value.Language });
    }
}
