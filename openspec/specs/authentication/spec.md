# Authentication Specification

## Purpose

Password-based sign-in that yields short-lived access tokens plus refresh tokens, keeps a browser signed in across restarts when asked, and lets users change or reset their password.

## Requirements

### Requirement: Sign in with email and password
The system SHALL authenticate a user from an email (or username) plus password and return an access token, a refresh token, and the access token lifetime.

#### Scenario: Valid credentials
- **WHEN** a visitor submits an email and the matching password
- **THEN** the response contains an access token and a refresh token, and the client loads the user's profile

#### Scenario: Invalid credentials
- **WHEN** a visitor submits an unknown email or a wrong password
- **THEN** authentication fails with an unauthorized result and no tokens are issued

#### Scenario: Repeated failures
- **WHEN** a visitor submits wrong passwords repeatedly for the same account
- **THEN** sign-in failures count toward account lockout

### Requirement: Bearer token authorization
Every non-public API operation and the real-time connection SHALL require a valid access token. Requests without a token, or with an expired or invalid one, SHALL be rejected as unauthorized.

#### Scenario: Missing token
- **WHEN** a client calls a protected endpoint with no access token
- **THEN** the request is rejected with 401

#### Scenario: Public catalog exception
- **WHEN** an unauthenticated client requests the public room catalog
- **THEN** the request succeeds, and no membership-specific fields are populated

### Requirement: Transparent token refresh
When a protected request fails because the access token expired, the client SHALL exchange its refresh token for a new token pair and retry the request once. Concurrent requests SHALL share a single in-flight refresh. If refresh fails, the client SHALL clear its credentials and return the user to sign-in.

#### Scenario: Expired access token
- **WHEN** a request returns 401 and a valid refresh token is stored
- **THEN** a new token pair is obtained and the original request is retried and succeeds

#### Scenario: Refresh rejected
- **WHEN** the refresh token is missing or rejected
- **THEN** stored credentials are cleared, the real-time connection is stopped, and the user is redirected to sign-in

### Requirement: Persistent login
Sign-in SHALL offer a "keep me signed in" choice. When selected, the refresh token SHALL survive closing and reopening the browser; when not selected, credentials SHALL be discarded when the browser session ends.

#### Scenario: Keep me signed in
- **WHEN** a user signs in with "keep me signed in" selected, closes the browser, and reopens the application
- **THEN** the session is restored without re-entering credentials

#### Scenario: Not persisted
- **WHEN** a user signs in without selecting "keep me signed in" and the browser session ends
- **THEN** reopening the application requires signing in again

#### Scenario: No idle logout
- **WHEN** a signed-in user leaves the application idle for a long period
- **THEN** the session is not terminated for inactivity alone

### Requirement: Sign out affects only the current browser
Signing out SHALL revoke only the session of the browser performing it and clear that browser's stored credentials. Sessions in other browsers or devices SHALL remain valid.

#### Scenario: Signing out one browser
- **WHEN** a user signed in on two browsers signs out in the first
- **THEN** the first browser returns to the sign-in screen and the second browser remains signed in and connected

### Requirement: Password change
A signed-in user SHALL be able to change their password by supplying the current password and a new password meeting the password policy. Passwords SHALL be stored only as salted hashes, never in recoverable form.

#### Scenario: Successful change
- **WHEN** a signed-in user submits the correct current password and a valid new password
- **THEN** the password is changed and the new password works on the next sign-in

#### Scenario: Wrong current password
- **WHEN** the supplied current password does not match
- **THEN** the change is rejected with an error and the stored password is unchanged

#### Scenario: Weak new password
- **WHEN** the new password fails the policy (minimum length, an uppercase letter, and a digit)
- **THEN** the change is rejected with a message stating the unmet requirements

### Requirement: Password reset by email
The system SHALL provide a "forgot password" flow that emails a reset code to a submitted address, and a reset screen that sets a new password from that code. The system SHALL NOT reveal whether the submitted address belongs to an account. No periodic forced password change SHALL be imposed.

#### Scenario: Requesting a reset
- **WHEN** a visitor submits an email address on the forgot-password screen
- **THEN** the screen confirms that a reset link was sent, regardless of whether the address is registered

#### Scenario: Completing a reset
- **WHEN** a visitor submits a valid reset code with a new password matching its confirmation and the policy
- **THEN** the password is changed and the visitor can sign in with it

#### Scenario: Invalid reset code
- **WHEN** the reset code is unknown or expired
- **THEN** the reset is rejected and the password is unchanged
