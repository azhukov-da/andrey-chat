# Chat Rooms Specification

## Purpose

Group chat rooms with a public catalog, invitation-only private rooms, and owner-controlled settings and lifecycle — the containers that hold membership and message history.

## Requirements

### Requirement: Room creation
Any authenticated user SHALL be able to create a group room with a required name, an optional description, and a visibility of public or private. The creator SHALL become the room's owner and its first member.

#### Scenario: Creating a room
- **WHEN** an authenticated user submits a unique room name with a visibility
- **THEN** the room is created, the creator holds the Owner role, member count is 1, and the room appears in the creator's room list

#### Scenario: Missing name
- **WHEN** a create or update request supplies a blank room name
- **THEN** the request is rejected with a name-required error

### Requirement: Room name uniqueness
Group room names SHALL be unique across all rooms that are not deleted. Uniqueness SHALL be enforced on creation and on rename.

#### Scenario: Duplicate name on creation
- **WHEN** a user creates a room whose name matches an existing live room
- **THEN** creation is rejected with a name-already-exists error

#### Scenario: Duplicate name on rename
- **WHEN** an owner renames a room to a name held by another live room
- **THEN** the update is rejected and the room keeps its previous name

#### Scenario: Reusing a deleted room's name
- **WHEN** a user creates a room with the name of a previously deleted room
- **THEN** creation succeeds

### Requirement: Room properties
A room SHALL carry a name, description, visibility, owner, membership with roles, and a list of banned users. Room reads SHALL report the current member count, the owner's username, and the requesting user's own role (absent when they are not a member).

#### Scenario: Reading a room
- **WHEN** a member requests a room
- **THEN** the response includes name, description, visibility, owner, member count, and the caller's role

#### Scenario: Non-member reads a public room
- **WHEN** a user who is not a member requests a public room
- **THEN** the room is returned with no role for the caller

### Requirement: Public room catalog
The system SHALL provide a searchable, paged catalog of public rooms showing each room's name, description, and current member count. Search SHALL match name or description, case-insensitively. Private rooms SHALL NOT appear in the catalog.

#### Scenario: Browsing the catalog
- **WHEN** a user opens the public rooms screen
- **THEN** public rooms are listed with description and member count, and further pages can be loaded

#### Scenario: Searching
- **WHEN** the user types a term into the catalog search box
- **THEN** the list narrows, after a short debounce, to rooms whose name or description contains the term

#### Scenario: Private rooms hidden
- **WHEN** a private room exists
- **THEN** it is absent from catalog results for every user, including its own members

#### Scenario: Already a member
- **WHEN** a catalog entry is a room the user already belongs to
- **THEN** the entry offers "Open" instead of "Join"

### Requirement: Joining public rooms
Any authenticated user SHALL be able to join a public room unless they are banned from it or already a member. Joining SHALL take effect immediately for real-time delivery.

#### Scenario: Joining
- **WHEN** an authenticated user joins a public room
- **THEN** they become a Member, the room appears in their room list, and they begin receiving that room's real-time events

#### Scenario: Banned from the room
- **WHEN** a user banned from a room attempts to join it
- **THEN** the request is rejected with a banned error

#### Scenario: Already a member
- **WHEN** an existing member attempts to join again
- **THEN** the request is rejected as already a member

#### Scenario: Private room join attempt
- **WHEN** a user attempts to join a private room directly
- **THEN** the request is rejected; access requires an invitation

### Requirement: Private room access
Private rooms SHALL be reachable only by their members. A non-member requesting a private room SHALL be refused, and the private rooms screen SHALL list only the rooms the user belongs to plus their pending invitations.

#### Scenario: Non-member requests a private room
- **WHEN** a user who is not a member requests a private room by id
- **THEN** the request is refused with a private-room error

#### Scenario: Private rooms screen
- **WHEN** a user opens the private rooms screen
- **THEN** it lists the private group rooms they are a member of and their pending invitations with accept and reject actions

### Requirement: Leaving rooms
A member SHALL be able to leave a group room at any time. The owner SHALL NOT be able to leave their own room; the owner's exit path is deleting it.

#### Scenario: Member leaves
- **WHEN** a non-owner member confirms leaving a room
- **THEN** their membership is removed, the room disappears from their room list, and they are navigated away from it

#### Scenario: Owner attempts to leave
- **WHEN** the owner attempts to leave their own room
- **THEN** the request is rejected with a message directing them to delete the room instead

#### Scenario: Leave control visibility
- **WHEN** the owner views their own group room
- **THEN** no "Leave room" control is offered, only "Delete room"

### Requirement: Room settings maintenance
Only the room owner SHALL be able to change a room's name, description, or visibility. Attempts by admins or members SHALL be refused, and the settings form SHALL present those fields read-only to non-owners.

#### Scenario: Owner saves settings
- **WHEN** the owner saves a new name, description, or visibility
- **THEN** the change is persisted and reflected in the room header, room lists, and the catalog

#### Scenario: Admin attempts to change settings
- **WHEN** a non-owner admin submits a settings change
- **THEN** the request is refused as not-owner

### Requirement: Room deletion
Only the owner SHALL be able to delete a room. Deletion SHALL make the room and its message history permanently inaccessible to all users, SHALL be confirmed before executing, and SHALL notify connected members so their view navigates away.

#### Scenario: Owner deletes a room
- **WHEN** the owner confirms deletion
- **THEN** the room disappears from every member's room list and its messages are no longer retrievable

#### Scenario: Member is viewing the deleted room
- **WHEN** a member has the room open at the moment it is deleted
- **THEN** they are navigated away from it and their room lists refresh

#### Scenario: Non-owner attempts deletion
- **WHEN** an admin or member attempts to delete the room
- **THEN** the request is refused

#### Scenario: Messaging a deleted room
- **WHEN** a client attempts to send a message or upload an attachment to a deleted room
- **THEN** the request is rejected as already deleted
