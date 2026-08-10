# User Accounts Specification

## Purpose

Self-service accounts for the chat application: registration with a unique email and immutable username, profile data other users see, and account removal.

## Requirements

### Requirement: Self-registration
The system SHALL allow any visitor to create an account by supplying an email address, a username, and a password. Email verification SHALL NOT be required to use the account.

#### Scenario: Successful registration
- **WHEN** a visitor submits an unused email, an unused username, and a password satisfying the password policy
- **THEN** the account is created and the visitor can immediately sign in with that email and password

#### Scenario: Missing field
- **WHEN** a registration request omits email, username, or password
- **THEN** registration is rejected with a field-level validation error and no account is created

### Requirement: Email and username uniqueness
The system SHALL reject registration when the email or the username is already in use by another account. Comparison SHALL be case-insensitive.

#### Scenario: Duplicate username
- **WHEN** a visitor registers with a username that already exists
- **THEN** registration is rejected with a message identifying the username as taken

#### Scenario: Duplicate email
- **WHEN** a visitor registers with an email that already exists
- **THEN** registration is rejected with a message identifying the email as taken

### Requirement: Username format and immutability
A username SHALL be 3–32 characters long, contain only letters, digits, `.`, `_`, or `-`, and SHALL NOT be equal to the account's email address. Once created, a username SHALL NOT be changeable by the user or by any exposed operation.

#### Scenario: Invalid username characters
- **WHEN** a visitor registers with a username containing spaces or other disallowed characters
- **THEN** registration is rejected with an invalid-username-format error

#### Scenario: Username equals email
- **WHEN** a visitor submits a username identical (ignoring case) to the submitted email
- **THEN** registration is rejected

#### Scenario: No rename path
- **WHEN** an authenticated user inspects the operations available on their own profile
- **THEN** the only mutable identity field is the display name; no operation changes the username

### Requirement: Display name
A user SHALL be able to set an optional display name of at most 50 characters. Where a display name exists, the UI SHALL show it in place of the username; the username SHALL remain visible as the stable handle.

#### Scenario: Setting a display name
- **WHEN** a user saves a new display name on their profile
- **THEN** subsequent message headers, member lists, and contact lists show the display name for that user

#### Scenario: No display name
- **WHEN** a user has no display name
- **THEN** their username is shown instead

### Requirement: Own profile retrieval
An authenticated user SHALL be able to retrieve their own profile containing id, username, display name, email, and account creation timestamp.

#### Scenario: Reading own profile
- **WHEN** an authenticated user requests their profile
- **THEN** the response contains their id, username, display name, email, and creation time

#### Scenario: Unauthenticated profile request
- **WHEN** an unauthenticated caller requests the current profile
- **THEN** the request is rejected as unauthorized

### Requirement: Account deletion
The system SHALL provide a "delete account" action available to the signed-in user, confirmed before it executes. On deletion the account SHALL be marked deleted, the user's stored attachment files SHALL be removed from storage, and the user's client session SHALL be signed out.

#### Scenario: Deleting an account
- **WHEN** a signed-in user confirms account deletion
- **THEN** the account is marked deleted, the user's uploaded attachment files are removed from file storage, and the browser is signed out and returned to the sign-in screen

#### Scenario: Deleting twice
- **WHEN** a delete request targets an account that is already marked deleted
- **THEN** the request is rejected as already deleted
