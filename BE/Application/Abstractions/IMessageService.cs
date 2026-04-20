using Application.Common;
using Application.Features.Messages.Dtos;

namespace Application.Abstractions;

public interface IMessageService
{
    Task<Result<MessageDto>> SendMessageAsync(Guid roomId, string text, Guid? replyToMessageId);
    Task<Result<CursorPaged<MessageDto>>> GetMessageHistoryAsync(Guid roomId, Guid? before, int limit);
    Task<Result> EditMessageAsync(Guid messageId, string text);
    Task<Result> DeleteMessageAsync(Guid messageId);
    Task<Result> MarkReadAsync(Guid roomId, Guid messageId);
}
