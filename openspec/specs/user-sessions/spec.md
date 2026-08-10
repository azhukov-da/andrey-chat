# User Sessions Specification

## Purpose

Visibility and control over where an account is signed in: a per-browser session record with device details, and the ability to revoke any of them individually.

## Requirements

### Requirement: Session registration per browser
The client SHALL register a session when a user signs in or when a stored login is restored without a session, and SHALL send that session's identifier with every subsequent API request. The recorded session SHALL capture the device description, user agent, and originating IP address, plus creation and last-seen timestamps.

#### Scenario: New sign-in registers a session
- **WHEN** a user signs in from a browser that has no session identifier stored
- **THEN** a session is created for that browser and its identifier is persisted locally and sent on later requests

#### Scenario: Restored login without a session
- **WHEN** a stored login is restored and no session identifier is present locally
- **THEN** a new session is registered for that browser

#### Scenario: Session registration failure is non-fatal
- **WHEN** session registration fails
- **THEN** sign-in still completes and the user can use the application

### Requirement: Listing active sessions
An authenticated user SHALL be able to list their own non-revoked sessions, most recently seen first, each showing device description, user agent, IP address, creation time, last-seen time, and whether it is the current browser's session.

#### Scenario: Viewing sessions
- **WHEN** a user opens the sessions screen
- **THEN** every active session of that user is listed with its device details and the current one is marked "Current"

#### Scenario: Only own sessions
- **WHEN** a user lists sessions
- **THEN** no session belonging to another account is returned

### Requirement: Revoking sessions
A user SHALL be able to revoke any of their own sessions. Revoking the current browser's session SHALL sign that browser out; revoking another session SHALL leave the current browser signed in and remove the revoked entry from the list.

#### Scenario: Revoking another session
- **WHEN** a user revokes a session other than the current one
- **THEN** that session is marked revoked, disappears from the list, and the current browser stays signed in

#### Scenario: Revoking the current session
- **WHEN** a user revokes the session marked "Current"
- **THEN** local credentials are cleared and the browser is returned to the sign-in screen

#### Scenario: Revoking a foreign session
- **WHEN** a revoke request names a session that does not belong to the caller
- **THEN** the request is rejected as not found and nothing is revoked
