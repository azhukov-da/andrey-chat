namespace Application.Common;

public record Error(string Code, string Message)
{
    public static Error None => new(string.Empty, string.Empty);
    public static Error NullValue => new("Error.NullValue", "The specified result value is null.");
}

public static class Errors
{
    public static class User
    {
        public static Error NotFound(string userId) => new("User.NotFound", $"User with ID '{userId}' was not found.");
        public static Error NotFoundByUsername(string username) => new("User.NotFoundByUsername", $"User with username '{username}' was not found.");
        public static Error AlreadyDeleted => new("User.AlreadyDeleted", "User account has been deleted.");
    }

    public static class Room
    {
        public static Error NotFound(Guid roomId) => new("Room.NotFound", $"Room with ID '{roomId}' was not found.");
        public static Error AlreadyDeleted => new("Room.AlreadyDeleted", "Room has been deleted.");
        public static Error NotMember => new("Room.NotMember", "You are not a member of this room.");
        public static Error AlreadyMember => new("Room.AlreadyMember", "You are already a member of this room.");
        public static Error NotOwner => new("Room.NotOwner", "You must be the room owner to perform this action.");
        public static Error NotOwnerOrAdmin => new("Room.NotOwnerOrAdmin", "You must be the room owner or admin to perform this action.");
        public static Error Banned => new("Room.Banned", "You have been banned from this room.");
        public static Error Frozen => new("Room.Frozen", "This room is frozen.");
        public static Error PrivateRoom => new("Room.PrivateRoom", "Cannot join a private room without an invitation.");
        public static Error NameAlreadyExists => new("Room.NameAlreadyExists", "A room with this name already exists.");
    }

    public static class Message
    {
        public static Error NotFound(Guid messageId) => new("Message.NotFound", $"Message with ID '{messageId}' was not found.");
        public static Error NotAuthor => new("Message.NotAuthor", "You can only edit or delete your own messages.");
        public static Error TooLarge => new("Message.TooLarge", "Message text exceeds maximum size of 3 KB.");
        public static Error AlreadyDeleted => new("Message.AlreadyDeleted", "Message has been deleted.");
    }

    public static class Attachment
    {
        public static Error NotFound(Guid attachmentId) => new("Attachment.NotFound", $"Attachment with ID '{attachmentId}' was not found.");
        public static Error FileTooLarge => new("Attachment.FileTooLarge", "File size exceeds maximum of 20 MB.");
        public static Error ImageTooLarge => new("Attachment.ImageTooLarge", "Image size exceeds maximum of 3 MB.");
        public static Error InvalidContentType => new("Attachment.InvalidContentType", "Invalid file content type.");
    }

    public static class Friendship
    {
        public static Error NotFound => new("Friendship.NotFound", "Friendship not found.");
        public static Error AlreadyFriends => new("Friendship.AlreadyFriends", "You are already friends with this user.");
        public static Error RequestAlreadyExists => new("Friendship.RequestAlreadyExists", "A friend request already exists.");
        public static Error CannotRequestSelf => new("Friendship.CannotRequestSelf", "You cannot send a friend request to yourself.");
    }

    public static class Block
    {
        public static Error AlreadyBlocked => new("Block.AlreadyBlocked", "User is already blocked.");
        public static Error NotBlocked => new("Block.NotBlocked", "User is not blocked.");
        public static Error CannotBlockSelf => new("Block.CannotBlockSelf", "You cannot block yourself.");
    }

    public static class Invitation
    {
        public static Error NotFound => new("Invitation.NotFound", "Invitation not found.");
        public static Error AlreadyProcessed => new("Invitation.AlreadyProcessed", "Invitation has already been processed.");
        public static Error NotRecipient => new("Invitation.NotRecipient", "You are not the recipient of this invitation.");
    }

    public static class Authorization
    {
        public static Error Forbidden => new("Authorization.Forbidden", "You do not have permission to perform this action.");
        public static Error Unauthorized => new("Authorization.Unauthorized", "You must be authenticated to perform this action.");
    }
}
