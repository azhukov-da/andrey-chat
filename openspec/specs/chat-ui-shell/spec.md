# Chat UI Shell Specification

## Purpose

The classic web-chat frame the features live in: a top menu, a central message area with input at the bottom, a collapsible rooms-and-contacts sidebar with a members panel, and modal dialogs for administration.

## Requirements

### Requirement: Application layout
The signed-in application SHALL present a top navigation bar, a central message area with the message input at its bottom, a rooms-and-contacts sidebar on the right, and — when a group room is open — a members/context panel on the right of the chat.

#### Scenario: Viewing a room
- **WHEN** a member opens a group room
- **THEN** the top navigation, the room header with name and description, the message list, the composer, the sidebar, and the members panel are all present

#### Scenario: Viewing a direct chat
- **WHEN** a user opens a direct chat
- **THEN** no room members/context panel is shown

### Requirement: Top navigation
The top navigation SHALL provide entries for public rooms, private rooms, contacts, profile, and sessions, plus a user menu carrying profile, sessions, and sign out. The active destination SHALL be visually marked and the aggregate unread count SHALL be visible.

#### Scenario: Navigating
- **WHEN** the user selects a top-level entry
- **THEN** the corresponding screen is shown and its navigation entry is marked active

#### Scenario: Signing out from the menu
- **WHEN** the user chooses sign out in the user menu
- **THEN** the current session is revoked and the sign-in screen is shown

### Requirement: Sidebar with accordion sections
The sidebar SHALL group rooms, pending invitations, and direct messages into individually collapsible sections. When a chat is open, the sections SHALL be compacted automatically, and the user SHALL still be able to expand any section manually.

#### Scenario: Compaction on entering a room
- **WHEN** the user opens a chat
- **THEN** the sidebar sections collapse to give the chat more room

#### Scenario: Leaving the chat view
- **WHEN** the user navigates to a screen with no active chat
- **THEN** the sidebar sections expand again

#### Scenario: Manual expansion
- **WHEN** the user clicks a collapsed section header while a chat is open
- **THEN** that section expands and stays expanded

### Requirement: Members and context panel
For an open group room the panel SHALL show room info (name, description, member count), the owner, the admins, and the remaining members, each with a presence indicator. Admin-only actions SHALL appear there for owners and admins.

#### Scenario: Panel contents
- **WHEN** a member views an open group room
- **THEN** the panel lists room info, owner, admins, and members with presence indicators

#### Scenario: Admin actions
- **WHEN** the viewer is the owner or an admin
- **THEN** the panel offers "Invite user" and "Manage room"; plain members see neither

### Requirement: Message list scrolling behavior
The message list SHALL follow new messages when the user is already at the bottom, SHALL NOT force a scroll when the user has scrolled up to read older messages, and SHALL load older history when the user reaches the top.

#### Scenario: At the bottom
- **WHEN** a new message arrives while the list is scrolled to the bottom
- **THEN** the list scrolls to reveal it

#### Scenario: Scrolled up
- **WHEN** a new message arrives while the user is reading older messages
- **THEN** the scroll position is left alone

#### Scenario: Reaching the top
- **WHEN** the user scrolls to the top and older messages exist
- **THEN** the next older page is loaded and a loading indicator is shown while it is in flight

### Requirement: Message composition controls
The composer SHALL support multiline entry, an emoji picker that inserts at the caret, an attach control, and a reply banner naming the pending reply target with a way to clear it. Enter SHALL send and Shift+Enter SHALL insert a newline. Per-chat drafts SHALL be retained while navigating between chats.

#### Scenario: Multiline entry
- **WHEN** the user presses Shift+Enter
- **THEN** a newline is inserted and the message is not sent

#### Scenario: Sending with Enter
- **WHEN** the user presses Enter with non-empty text
- **THEN** the message is sent and the input clears

#### Scenario: Inserting an emoji
- **WHEN** the user picks an emoji from the picker
- **THEN** it is inserted at the caret position, the picker closes, and focus returns to the input

#### Scenario: Draft retention
- **WHEN** the user types in one chat, navigates to another, and returns
- **THEN** the unsent text is still in the composer

### Requirement: Message row affordances
Each message SHALL show its author (for messages from others), timestamp, an "edited" marker when edited, and a reply action. Its author SHALL additionally see edit and delete actions. Own and others' messages SHALL be visually distinguished.

#### Scenario: Own message
- **WHEN** a user views a message they wrote
- **THEN** reply, edit, and delete actions are available on it

#### Scenario: Someone else's message
- **WHEN** a user views another user's message
- **THEN** the author is labelled and only the reply action is offered

#### Scenario: Inline edit
- **WHEN** the author activates edit
- **THEN** the text becomes an inline editable field where Enter saves and Escape cancels

### Requirement: Administration through modal dialogs
Administrative actions SHALL be reachable from menus and performed in modal dialogs. The room management dialog SHALL provide tabs for Members, Admins, Banned users, Invitations, and Settings, with member search in the Members tab. Destructive actions SHALL be confirmed before executing.

#### Scenario: Opening room management
- **WHEN** an owner or admin chooses "Manage room"
- **THEN** a modal dialog opens showing the five tabs

#### Scenario: Searching members
- **WHEN** the admin types into the Members tab search box
- **THEN** the table narrows to members whose username or display name matches, and reports when nothing matches

#### Scenario: Confirming a destructive action
- **WHEN** the user triggers room deletion, leaving a room, removing a contact, or deleting their account
- **THEN** a confirmation is required before the action is performed

### Requirement: Unauthenticated access and routing
Unauthenticated visitors SHALL be able to reach the sign-in, register, forgot-password, and reset-password screens, and SHALL be sent to sign-in when they request an application screen. The application root SHALL redirect to the rooms screen.

#### Scenario: Reaching a protected screen while signed out
- **WHEN** an unauthenticated visitor requests an application screen
- **THEN** they are shown the sign-in screen, and after signing in are returned to the screen they asked for

#### Scenario: Root redirect
- **WHEN** a signed-in user opens the application root
- **THEN** they are redirected to the rooms screen

### Requirement: Loading and error feedback
Each screen SHALL show a loading indicator while its data is in flight and a readable message when loading or an action fails, rather than an empty or frozen view.

#### Scenario: Data loading
- **WHEN** a screen's data has not yet arrived
- **THEN** a loading indicator is displayed

#### Scenario: Failed action
- **WHEN** an action such as saving room settings or sending an invitation fails
- **THEN** the reason is displayed near the control that triggered it

#### Scenario: Missing room
- **WHEN** the user opens a chat that does not exist or they cannot access
- **THEN** a "Room not found" message is shown instead of an empty chat
