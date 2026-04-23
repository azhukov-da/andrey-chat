using Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Models;

namespace Web.Controllers;

[Authorize]
[ApiController]
[Route("api/rooms/{roomId:guid}/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessagesController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(Guid roomId, [FromQuery] Guid? before, [FromQuery] int limit = 50)
    {
        var result = await _messageService.GetMessageHistoryAsync(roomId, before, limit);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Send(Guid roomId, [FromBody] SendMessageRequest request)
    {
        var result = await _messageService.SendMessageAsync(roomId, request.Text, request.ReplyToMessageId);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(Guid roomId, [FromBody] MarkReadRequest request)
    {
        var result = await _messageService.MarkReadAsync(roomId, request.MessageId);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }
}

[Authorize]
[ApiController]
[Route("api/messages")]
public class MessageActionsController : ControllerBase
{
    private readonly IMessageService _messageService;

    public MessageActionsController(IMessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditMessageRequest request)
    {
        var result = await _messageService.EditMessageAsync(id, request.Text);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _messageService.DeleteMessageAsync(id);
        
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return NoContent();
    }
}


