# Realtime Delivery Specification

## Purpose

The push channel every live feature depends on: one authenticated connection per client that carries messages, presence, membership, and notification events, survives brief network loss, and delivers only what the recipient is entitled to see.

## Requirements

### Requirement: Single authenticated realtime connection
A signed-in client SHALL maintain exactly one real-time connection, established after the profile is loaded and torn down on sign-out. The connection SHALL require the same access token as the HTTP API, and unauthenticated connection attempts SHALL be rejected.

#### Scenario: Connection after sign-in
- **WHEN** a user signs in and their profile loads
- **THEN** one real-time connection is established and event handlers are registered once

#### Scenario: Sign-out
- **WHEN** the user signs out or credentials are cleared after a failed refresh
- **THEN** the connection is stopped and handlers are removed

#### Scenario: Unauthenticated connect
- **WHEN** a client attempts to connect without a valid token
- **THEN** the connection is refused

### Requirement: Automatic reconnection
The client SHALL automatically attempt to reconnect after an unexpected disconnect, with escalating delays, and SHALL re-establish its event subscriptions and refresh presence for the visible users once reconnected.

#### Scenario: Transient network loss
- **WHEN** the connection drops and the network returns shortly afterwards
- **THEN** the client reconnects without user action and continues receiving events

#### Scenario: Presence after reconnect
- **WHEN** the connection is re-established
- **THEN** presence for the users currently displayed is requested again so stale indicators are corrected

### Requirement: Recipient scoping
Events SHALL be delivered only to the parties entitled to them: chat events to the participants of that chat, and personal notifications to the target user's own connections. A client SHALL start receiving a chat's events as soon as it gains membership, on all of that user's open connections.

#### Scenario: Room message fan-out
- **WHEN** a message is posted in a room
- **THEN** every connection belonging to a member of that room receives it, and no connection outside the room does

#### Scenario: Joining mid-session
- **WHEN** a user joins a room, accepts an invitation, or a direct chat is created for them
- **THEN** their already-open connections begin receiving that chat's events without reconnecting

#### Scenario: Personal notification
- **WHEN** a friend request or unread update targets a user
- **THEN** only that user's own connections receive it

#### Scenario: Subscriptions on connect
- **WHEN** a client connects
- **THEN** it is subscribed to its personal notifications and to every chat it is currently a member of

### Requirement: Pushed event kinds
The channel SHALL push at least: new messages, message edits, message deletions, presence changes, room membership changes, room deletion, friend requests, unread updates, and typing start/stop. Each SHALL carry enough data for the client to update its view without refetching the whole collection.

#### Scenario: Edit is applied in place
- **WHEN** a message edit event arrives
- **THEN** the affected message's text and edited marker update in place in every open view of that chat

#### Scenario: Deletion is applied in place
- **WHEN** a message deletion event arrives
- **THEN** the affected message renders as deleted with its text cleared

#### Scenario: Membership change refreshes derived views
- **WHEN** a membership change event arrives for a room
- **THEN** the recipient's room list and that room's member list are refreshed

#### Scenario: Room deletion while viewing
- **WHEN** a room deletion event arrives for the room the user is viewing
- **THEN** the room lists refresh and the user is navigated away from the deleted room

### Requirement: Delivery latency
A message accepted by the server SHALL reach connected recipients within 3 seconds under normal load, and presence transitions within 2 seconds.

#### Scenario: Message delivery
- **WHEN** a member posts a message while another member is connected
- **THEN** the message appears in the other member's open chat within 3 seconds

### Requirement: Duplicate suppression
A client SHALL not display the same message twice when it both receives the pushed event and the response to its own send. Locally inserted messages SHALL be reconciled by message id.

#### Scenario: Own message echo
- **WHEN** a user sends a message and the pushed event for it arrives
- **THEN** exactly one copy of the message is shown

### Requirement: Operation failures surface as errors
An operation invoked over the real-time channel that fails validation or authorization SHALL return an error carrying the failure's message rather than silently succeeding.

#### Scenario: Refused send
- **WHEN** a client invokes a send that the server refuses
- **THEN** the invocation fails with an error message describing the reason
