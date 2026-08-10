# User Presence Specification

## Purpose

Live online / AFK / offline indicators for contacts and room members, derived from real-time connections and browser activity, and resilient to a user having several tabs open.

## Requirements

### Requirement: Presence states
The system SHALL expose exactly three presence states for a user — `online`, `afk` (away), and `offline` — and SHALL treat a user with no live real-time connection as `offline`.

#### Scenario: Connected and active
- **WHEN** a user has a live connection reporting recent activity
- **THEN** their state is `online`

#### Scenario: Connected but idle
- **WHEN** a user's only live connections have reported no activity for longer than the idle threshold
- **THEN** their state is `afk`

#### Scenario: No connection
- **WHEN** a user has no live connection
- **THEN** their state is `offline`

### Requirement: Activity heartbeat and idle threshold
The client SHALL report activity to the server on a periodic heartbeat, marking itself active only when the page is visible and user input (pointer, keyboard, touch, or wheel) occurred within the last 60 seconds. The server SHALL classify a user as `afk` once no connection has reported active within that 60-second window.

#### Scenario: Going idle
- **WHEN** a signed-in user stops interacting with the page for more than a minute
- **THEN** the next heartbeat reports inactive and observers see the user as `afk`

#### Scenario: Returning from idle
- **WHEN** the user interacts with the page again, or the tab becomes visible
- **THEN** an activity report is sent and observers see the user as `online`

#### Scenario: Hidden tab
- **WHEN** the tab is hidden
- **THEN** the heartbeat reports inactive even if the machine is otherwise in use

### Requirement: Multi-tab presence
The application SHALL work correctly with several tabs of the same account open. A single tab SHALL be elected to send heartbeats on behalf of the browser; leadership SHALL transfer when the leading tab closes. A user SHALL become `offline` only when all their connections are gone.

#### Scenario: Two tabs open
- **WHEN** a user has two tabs open and interacts with either one
- **THEN** other users see the account as `online`, not duplicated or conflicting

#### Scenario: Leader tab closes
- **WHEN** the tab currently sending heartbeats is closed while another tab remains open
- **THEN** the remaining tab takes over heartbeating and presence continues to be reported

#### Scenario: Closing the last tab
- **WHEN** the last tab of an account is closed
- **THEN** the account transitions to `offline` for all observers

### Requirement: Presence change broadcast
Presence transitions SHALL be pushed to connected clients as they happen — on connect, on disconnect, and on each activity report — so observers converge without polling, with latency under 2 seconds under normal load.

#### Scenario: Contact comes online
- **WHEN** a contact establishes a real-time connection
- **THEN** the observing client's presence indicator for that contact turns online without a page reload

#### Scenario: Contact disconnects
- **WHEN** a contact's connection drops
- **THEN** the observing client's indicator for that contact updates promptly

### Requirement: Presence lookup on demand
A client SHALL be able to request the current state of a set of users in one call, and SHALL do so when a view first needs presence for users it has received no events for — including after a reconnect.

#### Scenario: Hydrating a freshly opened view
- **WHEN** a client opens a view listing direct-message partners or room members
- **THEN** it requests their presence in bulk and renders the returned states

#### Scenario: After reconnect
- **WHEN** the real-time connection is re-established
- **THEN** presence for the currently displayed users is requested again

### Requirement: Presence indicators in the UI
The UI SHALL render a presence indicator beside each contact, direct-message partner, and room member, visually distinguishing online, AFK, and offline, and SHALL show a textual status in the room member management table.

#### Scenario: Member list indicators
- **WHEN** a user views a room's member panel
- **THEN** each member is shown with an indicator reflecting their current presence state

#### Scenario: Unknown presence
- **WHEN** no presence information has been received for a listed user
- **THEN** that user is displayed as offline
