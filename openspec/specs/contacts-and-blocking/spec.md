# Contacts and Blocking Specification

## Purpose

Per-user contact lists built on mutually confirmed friend requests, plus a one-sided block that cuts off further private contact while preserving existing history.

## Requirements

### Requirement: Personal contact list
Each user SHALL have a personal contact list showing confirmed friends and incoming pending requests as separate groups, each entry carrying username, display name, and presence indicator.

#### Scenario: Viewing contacts
- **WHEN** a user opens the contacts screen
- **THEN** confirmed friends are listed, and any incoming pending requests are listed separately above them

#### Scenario: Outgoing requests are not listed as contacts
- **WHEN** a user has sent a request that the recipient has not answered
- **THEN** that user does not appear in the sender's contact list until the request is accepted

### Requirement: Sending a friend request
A user SHALL be able to send a friend request by username or email address, optionally including a short message. A request SHALL be rejected when the target does not exist, is the sender, or a friendship or pending request already exists between the two.

#### Scenario: Request by username
- **WHEN** a user submits an existing username in the add-contact dialog
- **THEN** a pending friend request is created and the recipient is notified in real time

#### Scenario: Request with a message
- **WHEN** the sender includes optional text with the request
- **THEN** the text is stored with the request and delivered in the notification

#### Scenario: Unknown target
- **WHEN** the submitted username or email matches no account
- **THEN** the request is rejected with a user-not-found error

#### Scenario: Duplicate request
- **WHEN** a request or friendship already exists between the two users
- **THEN** the new request is rejected as already existing

#### Scenario: Self request
- **WHEN** a user sends a request to themselves
- **THEN** the request is rejected

### Requirement: Confirming friendship
A friendship SHALL become effective only when the recipient accepts. The recipient SHALL be able to accept or reject; the sender SHALL be able to do neither on their own request.

#### Scenario: Accepting
- **WHEN** the recipient accepts a pending request
- **THEN** the friendship becomes accepted, records the acceptance time, and both users see each other as contacts

#### Scenario: Rejecting
- **WHEN** the recipient rejects a pending request
- **THEN** the request is removed and neither user appears in the other's contact list

#### Scenario: Sender cannot self-accept
- **WHEN** the requesting user attempts to accept or reject their own request
- **THEN** the operation is rejected

### Requirement: Removing a contact
A user SHALL be able to remove another user from their contact list, after confirmation. Removal SHALL end the friendship for both sides.

#### Scenario: Removing a friend
- **WHEN** a user confirms removal of a contact
- **THEN** the friendship record is deleted and neither user lists the other as a contact

### Requirement: Blocking a user
A user SHALL be able to block another user. While a block is in force in either direction, the two users SHALL NOT be able to start a new direct chat or send new direct messages to each other, and their existing direct chat SHALL be marked frozen. A user SHALL NOT block themselves, and blocking an already blocked user SHALL be rejected.

#### Scenario: Blocking from the contact list
- **WHEN** a user blocks a contact
- **THEN** any direct chat between them is marked frozen and further direct messages from either side are refused

#### Scenario: Blocked user attempts to write
- **WHEN** the blocked user sends a message in the existing direct chat
- **THEN** the send is refused as forbidden

#### Scenario: History remains readable
- **WHEN** either user opens the frozen direct chat
- **THEN** the existing message history is still visible

#### Scenario: Blocking oneself
- **WHEN** a user attempts to block their own account
- **THEN** the request is rejected

### Requirement: Unblocking
The user who created a block SHALL be able to remove it. Unblocking SHALL unfreeze the direct chats between the two users, restoring the ability to exchange messages provided they are still friends.

#### Scenario: Unblocking
- **WHEN** the blocker removes the block
- **THEN** their shared direct chats are no longer frozen and messaging works again

#### Scenario: Unblocking someone who is not blocked
- **WHEN** an unblock request names a user who is not blocked by the caller
- **THEN** the request is rejected as not blocked
