# Unread Notifications Specification

## Purpose

Lightweight signals that something needs attention: unread badges next to rooms and contacts, a read marker per chat, typing indicators, and real-time notices for friend requests and invitations.

## Requirements

### Requirement: Unread indicators
The UI SHALL show an unread badge with a count next to each room or direct chat that has messages the user has not seen, and SHALL surface an aggregate unread count in the top navigation. Chats with no unread messages SHALL show no badge.

#### Scenario: Message arrives in a background chat
- **WHEN** a message arrives for a chat the user does not currently have open
- **THEN** that chat's unread count increases and its badge appears in the sidebar

#### Scenario: Message arrives in the open chat
- **WHEN** a message arrives for the chat the user is currently viewing
- **THEN** no unread badge is raised for it

#### Scenario: Aggregate count
- **WHEN** two chats each hold unread messages
- **THEN** the top navigation shows the combined count

### Requirement: Clearing unread state
Opening a chat SHALL clear its unread indicator and record the newest visible message as the user's read position for that chat, so the position survives reconnects and other devices.

#### Scenario: Opening a chat clears the badge
- **WHEN** the user opens a chat with unread messages
- **THEN** its badge disappears and the newest message is recorded as read

#### Scenario: Read position recorded per membership
- **WHEN** the read position is recorded
- **THEN** it is stored against the user's membership of that chat

#### Scenario: Real-time channel unavailable
- **WHEN** the read position cannot be sent over the real-time channel
- **THEN** it is sent over the HTTP API instead

#### Scenario: Marking read in a chat one is not in
- **WHEN** a mark-read request names a chat the caller is not a member of
- **THEN** the request is refused as not-a-member

### Requirement: Typing indicators
While a user is composing in a chat, the other participants of that chat SHALL be notified that the user is typing, and notified again when composing stops or a message is sent. The notice SHALL NOT be sent back to the composing user.

#### Scenario: Composing
- **WHEN** a user types into the message input of a chat
- **THEN** the other participants of that chat receive a typing notice naming the user and chat

#### Scenario: Sending stops typing
- **WHEN** the user sends the message
- **THEN** a stopped-typing notice is sent to the other participants

### Requirement: Friend request notification
When a friend request is created, the recipient's connected clients SHALL be notified in real time with the requester's identity and any message, and the recipient's contact list SHALL refresh so the pending request appears without a reload.

#### Scenario: Receiving a request while online
- **WHEN** another user sends the recipient a friend request
- **THEN** the recipient's contact list refreshes and shows the pending request

### Requirement: Invitation visibility
Pending room invitations SHALL be surfaced to the invited user in the sidebar with a count and accept/reject actions, and SHALL also be listed on the private rooms screen. The list SHALL refresh periodically so invitations appear without a reload.

#### Scenario: Invitation arrives
- **WHEN** a user is invited to a room while the application is open
- **THEN** within the refresh interval the invitation appears in the sidebar with the room name, the inviter, and Accept/Reject actions

#### Scenario: No pending invitations
- **WHEN** the user has no pending invitations
- **THEN** the invitations section is not shown in the sidebar

#### Scenario: Answering an invitation updates lists
- **WHEN** the user accepts or rejects an invitation
- **THEN** the invitation list and, on accept, the room list refresh
