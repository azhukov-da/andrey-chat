# Room Moderation Specification

## Purpose

Owner and admin control over who is in a room: promoting and demoting admins, removing and banning members, maintaining the ban list, and inviting users into private rooms.

## Requirements

### Requirement: Room roles
Each room SHALL have exactly one owner plus zero or more admins and members. The owner SHALL always hold full admin authority and SHALL NOT be demoted, banned, or removed by anyone.

#### Scenario: Owner privileges are permanent
- **WHEN** any user attempts to remove the owner's admin status, ban the owner, or remove the owner from the room
- **THEN** the attempt is refused

#### Scenario: Role display
- **WHEN** an admin opens the room management dialog
- **THEN** every member is listed with their role (Owner, Admin, or Member) and current presence status

### Requirement: Moderation requires admin authority
Every moderation operation SHALL require the caller to be the owner or an admin of the target room. Callers who are not members, or who are plain members, SHALL be refused.

#### Scenario: Member attempts moderation
- **WHEN** a plain member calls a moderation operation
- **THEN** the request is refused as not-owner-or-admin

#### Scenario: Outsider attempts moderation
- **WHEN** a user who is not a member of the room calls a moderation operation
- **THEN** the request is refused

### Requirement: Managing admins
Admin status SHALL be grantable and revocable on members of the room. Granting to a user who is already an admin SHALL be a no-op success. The owner's role SHALL be untouchable. In the UI, promoting and demoting admins SHALL be offered to the owner.

#### Scenario: Promoting a member
- **WHEN** the owner promotes a member
- **THEN** that member's role becomes Admin, connected room members are notified, and the member list reflects the new role

#### Scenario: Demoting an admin
- **WHEN** an admin's status is revoked
- **THEN** their role becomes Member and connected room members are notified

#### Scenario: Promoting an existing admin
- **WHEN** a promote request targets a user who is already an admin
- **THEN** the request succeeds without changing anything

#### Scenario: Target is not a member
- **WHEN** a role change targets a user who is not a member of the room
- **THEN** the request is refused as not-a-member

### Requirement: Removal is a ban
Removing a member from a room SHALL both drop their membership and record a ban, so the removed user cannot rejoin until the ban is lifted. Admins SHALL NOT remove other admins; only the owner may.

#### Scenario: Removing a member
- **WHEN** an admin removes a member
- **THEN** the membership is deleted, a ban entry naming the acting admin is recorded, and connected room members are notified

#### Scenario: Removed user tries to rejoin
- **WHEN** the removed user attempts to join the public room again
- **THEN** the join is refused as banned

#### Scenario: Admin removing an admin
- **WHEN** a non-owner admin attempts to remove another admin
- **THEN** the request is refused as forbidden

### Requirement: Banning users
An admin SHALL be able to ban a user from a room with an optional reason, whether or not they are currently a member. Banning SHALL remove any existing membership and record who applied the ban and when. Admins SHALL NOT ban other admins; only the owner may.

#### Scenario: Banning a member with a reason
- **WHEN** an admin bans a member and supplies a reason
- **THEN** the member is removed from the room and a ban entry records the banned user, the acting admin, the reason, and the timestamp

#### Scenario: Banning an already banned user
- **WHEN** a ban targets a user who already has a ban entry for that room
- **THEN** the operation succeeds without creating a duplicate entry

#### Scenario: Admin banning an admin
- **WHEN** a non-owner admin attempts to ban another admin
- **THEN** the request is refused as forbidden

### Requirement: Ban list visibility and unbanning
Admins SHALL be able to view the room's ban list — each entry showing the banned username, who banned them, the date and time, and any reason — and SHALL be able to lift any entry.

#### Scenario: Viewing bans
- **WHEN** an admin opens the banned-users tab
- **THEN** each ban is listed with banned user, banning user, timestamp, reason if present, and an Unban action

#### Scenario: Unbanning
- **WHEN** an admin unbans a user
- **THEN** the ban entry is removed, connected room members are notified, and the user may join the room again if it is public

#### Scenario: Unbanning someone not banned
- **WHEN** an unban request names a user with no ban entry
- **THEN** the operation succeeds without change

### Requirement: Loss of room access
When a user loses membership of a room — by leaving, removal, or ban — they SHALL immediately lose the ability to read that room's message history and to download its attachments through the application.

#### Scenario: Reading history after removal
- **WHEN** a removed user requests the room's message history
- **THEN** the request is refused as not-a-member

#### Scenario: Downloading an attachment after removal
- **WHEN** a removed user requests an attachment stored in that room, including one they uploaded themselves
- **THEN** the download is refused as not-a-member

### Requirement: Room invitations
Owners and admins SHALL be able to invite a user to a room by username. An invitation SHALL be refused when the username is unknown, names the inviter, names an existing member, names a banned user, or duplicates a pending invitation.

#### Scenario: Sending an invitation
- **WHEN** an admin submits an existing username on the invitations tab
- **THEN** a pending invitation is created and confirmation naming the invitee is shown

#### Scenario: Inviting a banned user
- **WHEN** an admin invites a user banned from that room
- **THEN** the invitation is refused as banned

#### Scenario: Duplicate invitation
- **WHEN** a pending invitation for that user and room already exists
- **THEN** the new invitation is refused as already existing

#### Scenario: Non-admin attempts to invite
- **WHEN** a plain member opens the invitations tab
- **THEN** they are told only admins can send invitations

### Requirement: Responding to invitations
A user SHALL see their pending invitations, each naming the room and the inviter, and SHALL be able to accept or reject them. Accepting SHALL make them a Member and take effect immediately for real-time delivery. Only the invited user SHALL be able to answer an invitation, and only while it is pending.

#### Scenario: Accepting an invitation
- **WHEN** the invited user accepts
- **THEN** the invitation is marked accepted, the user becomes a Member, the room appears in their room lists, and they start receiving its real-time events

#### Scenario: Rejecting an invitation
- **WHEN** the invited user rejects
- **THEN** the invitation is marked rejected and no membership is created

#### Scenario: Answering someone else's invitation
- **WHEN** a user other than the invitee attempts to accept or reject
- **THEN** the request is refused as not-the-recipient

#### Scenario: Answering twice
- **WHEN** an accept or reject targets an invitation that is no longer pending
- **THEN** the request is refused as already processed

#### Scenario: Invitation to a deleted room
- **WHEN** the invited user accepts an invitation whose room has been deleted
- **THEN** the request is refused and no membership is created

#### Scenario: Banned before accepting
- **WHEN** the invited user was banned from the room after the invitation was sent
- **THEN** accepting is refused as banned
