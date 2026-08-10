# Direct Messages Specification

## Purpose

One-to-one conversations between confirmed friends, modelled as a two-participant chat so they gain the same messaging, attachment, and notification behavior as rooms without any moderation roles.

## Requirements

### Requirement: Direct chats are two-participant chats
A direct chat SHALL behave, from the user's and the feature's point of view, exactly like a room chat with a fixed participant list of two users: the same message composition, replies, editing, deletion, attachments, history paging, and unread indicators apply. Direct chats SHALL be private and SHALL NOT appear in the public catalog.

#### Scenario: Feature parity
- **WHEN** a user opens a direct chat
- **THEN** they can send text, emoji, replies, and attachments, and browse history exactly as in a group room

#### Scenario: No moderation roles
- **WHEN** a user views a direct chat
- **THEN** no owner or admin roles, member management, ban list, or room settings are offered

#### Scenario: Not in the catalog
- **WHEN** any user browses the public room catalog
- **THEN** no direct chat appears in the results

### Requirement: Opening a direct chat requires friendship
A direct chat SHALL be openable only between users who are confirmed friends with no block in either direction. Opening SHALL return the existing chat when one exists and create it otherwise, and both participants SHALL start receiving its real-time events immediately.

#### Scenario: Messaging a friend for the first time
- **WHEN** a user chooses "Message" on a confirmed contact
- **THEN** a two-member direct chat is created and opened, and both participants receive its real-time events

#### Scenario: Reopening an existing chat
- **WHEN** the same pair opens a direct chat again
- **THEN** the existing chat with its history is returned instead of a new one

#### Scenario: Not friends
- **WHEN** a user attempts to open a direct chat with someone who is not a confirmed friend
- **THEN** the request is refused with a friendship-not-accepted error

#### Scenario: Blocked in either direction
- **WHEN** either user has blocked the other
- **THEN** opening a direct chat is refused

#### Scenario: Chat with self
- **WHEN** a user attempts to open a direct chat with their own username
- **THEN** the request is refused

### Requirement: Frozen direct chats
A direct chat between users where a block is in force SHALL be marked frozen. New messages SHALL be refused for both participants while frozen, and the existing history SHALL remain readable.

#### Scenario: Sending in a frozen chat
- **WHEN** either participant sends a message while a block is in force
- **THEN** the send is refused as forbidden

#### Scenario: Reading a frozen chat
- **WHEN** a participant opens a frozen direct chat
- **THEN** the full existing history is displayed

#### Scenario: Unblocking restores messaging
- **WHEN** the block is removed
- **THEN** the chat is no longer frozen and both participants can send messages again

### Requirement: Direct chats in the sidebar
Direct chats SHALL be listed separately from group rooms in the sidebar, each entry identifying the other participant with their presence indicator and unread badge.

#### Scenario: Listing direct chats
- **WHEN** a user has one or more direct chats
- **THEN** they appear under a "Direct Messages" section showing the other participant's name, presence, and unread count when non-zero
