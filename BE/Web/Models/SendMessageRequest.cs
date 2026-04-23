namespace Web.Models;

public record SendMessageRequest(string Text, Guid? ReplyToMessageId);
