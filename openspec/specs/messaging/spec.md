# Messaging Specification

## Purpose

Sending, editing, deleting, quoting, and reading chat messages, with persistent history that loads incrementally and behaves identically in group rooms and direct chats.

## Requirements

### Requirement: Sending messages
A member of a chat SHALL be able to post a message containing UTF-8 plain text, including newlines and emoji. Non-members, banned users, and senders in a deleted room SHALL be refused. Sending SHALL be possible over both the real-time channel and the HTTP API, with identical rules.

#### Scenario: Member sends text
- **WHEN** a member submits non-empty text in a room they belong to
- **THEN** the message is persisted with its author and creation time and fanned out to everyone in the room

#### Scenario: Multiline and emoji
- **WHEN** the sender composes text with line breaks and emoji characters
- **THEN** the text is stored and displayed with its line breaks and emoji intact

#### Scenario: Non-member sends
- **WHEN** a user who is not a member attempts to post to a room
- **THEN** the send is refused as not-a-member

#### Scenario: Banned member sends
- **WHEN** a user with a ban entry for the room attempts to post
- **THEN** the send is refused as banned

### Requirement: Message size limit
Message text SHALL be limited to 3072 bytes (3 KB) of UTF-8. The client SHALL show the byte count and block sending once the limit is exceeded, and the server SHALL reject oversized text on both send and edit.

#### Scenario: Oversized send
- **WHEN** text whose UTF-8 encoding exceeds 3072 bytes is submitted
- **THEN** the request is rejected as too large and no message is stored

#### Scenario: Client-side warning
- **WHEN** the composer content exceeds the limit
- **THEN** the byte count is shown in an error style and the Send action is disabled

### Requirement: Replies
A user SHALL be able to send a message as a reply to an existing message in the same chat. The referenced message SHALL be quoted in the replying message's display, showing its author and a truncated preview of its content. A reply SHALL be refused if the referenced message does not exist in that chat.

#### Scenario: Composing a reply
- **WHEN** a user picks reply on a message and sends text
- **THEN** the new message records the reference and renders with the quoted original above its text

#### Scenario: Quoting a deleted message
- **WHEN** the referenced message has been deleted
- **THEN** the quote renders as "Message deleted" instead of its former text

#### Scenario: Quoting an attachment message
- **WHEN** the referenced message carries an attachment and no text
- **THEN** the quote identifies the attachment by file name

#### Scenario: Reply target in another room
- **WHEN** the referenced message id does not belong to the target chat
- **THEN** the send is refused with a message-not-found error

#### Scenario: Cancelling a reply
- **WHEN** the user dismisses the reply banner in the composer
- **THEN** the pending reference is cleared and the next message is sent without one

### Requirement: Editing messages
A user SHALL be able to edit their own messages. Editing SHALL record the edit time, push the new text to everyone in the chat, and cause the message to display a subdued "edited" marker. Editing another user's message or a deleted message SHALL be refused.

#### Scenario: Author edits
- **WHEN** the author saves new text for their message
- **THEN** the stored text and edit time are updated, all clients in the chat show the new text, and an "edited" marker appears

#### Scenario: Non-author edits
- **WHEN** a user who is not the author attempts to edit a message
- **THEN** the request is refused as not-the-author

#### Scenario: Editing a deleted message
- **WHEN** an edit targets a message that has been deleted
- **THEN** the request is refused as already deleted

### Requirement: Deleting messages
A message SHALL be deletable by its author, and in group rooms also by the room's owner or admins. Deleted messages SHALL stop exposing their text and SHALL render as a "Message deleted" placeholder for everyone. Deleted messages need not be recoverable.

#### Scenario: Author deletes
- **WHEN** the author deletes their message
- **THEN** the message is marked deleted, its text is no longer served, and all clients render the placeholder

#### Scenario: Admin deletes another user's message
- **WHEN** a room owner or admin deletes a message written by someone else in that room
- **THEN** the deletion succeeds and is pushed to all clients in the room

#### Scenario: Unauthorized delete
- **WHEN** a plain member attempts to delete another user's message
- **THEN** the request is refused

#### Scenario: Deleting twice
- **WHEN** a delete targets an already deleted message
- **THEN** the request is refused as already deleted

### Requirement: Persistent history in chronological order
Messages SHALL be stored persistently and remain available indefinitely. History SHALL be presented in chronological order, oldest to newest, and messages sent while a recipient is offline SHALL be delivered when they next open the chat.

#### Scenario: Reading history
- **WHEN** a member opens a chat
- **THEN** the most recent messages are displayed in chronological order with author, timestamp, and any attachments

#### Scenario: Offline recipient
- **WHEN** a user receives messages while disconnected and later signs in
- **THEN** those messages are present in the chat history

#### Scenario: History across restarts
- **WHEN** the application is restarted
- **THEN** previously sent messages are still retrievable

### Requirement: Incremental history loading
History SHALL be retrieved in pages using a cursor over the newest-first ordering, reporting whether older messages remain. The client SHALL load the next page automatically when the user scrolls to the top of the list, so arbitrarily long histories can be browsed.

#### Scenario: Scrolling back
- **WHEN** the user scrolls to the top of the message list and older messages exist
- **THEN** the next older page is fetched and prepended, with a loading indicator while it is in flight

#### Scenario: Reaching the beginning
- **WHEN** no older messages remain
- **THEN** no further fetches are made

#### Scenario: Large history remains usable
- **WHEN** a room holds tens of thousands of messages
- **THEN** opening it and scrolling back stay responsive because only rendered rows and fetched pages are held

### Requirement: History access control
Message history SHALL be readable only by current members of the chat. Requests from non-members SHALL be refused regardless of whether the requester was a member in the past.

#### Scenario: Non-member requests history
- **WHEN** a user who is not a member requests a chat's history
- **THEN** the request is refused as not-a-member
