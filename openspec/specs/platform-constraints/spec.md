# Platform Constraints Specification

## Purpose

The system-wide limits and qualities the chat must hold to: how many users and messages it carries, how fast it must respond, how durably it stores data, and which consistency guarantees moderation depends on.

## Requirements

### Requirement: Capacity
The system SHALL support at least 300 simultaneously connected users and at least 1000 participants in a single chat room. A user SHALL be able to belong to an unbounded number of rooms; typical sizing assumes about 20 rooms and 50 contacts per user.

#### Scenario: Concurrent users
- **WHEN** 300 users are connected simultaneously
- **THEN** messaging, presence, and history browsing continue to work within the stated latency targets

#### Scenario: Large room
- **WHEN** a room holds 1000 members
- **THEN** posting to it and listing its members succeed

### Requirement: Performance targets
Message delivery to connected recipients SHALL complete within 3 seconds of the sender's message being accepted, and presence transitions SHALL propagate within 2 seconds. A room holding at least 10,000 messages SHALL remain usable to open and scroll.

#### Scenario: Very large history
- **WHEN** a member opens a room containing more than 10,000 messages
- **THEN** the most recent page renders promptly and scrolling back loads older pages without freezing the UI

### Requirement: Durable persistence
Messages, memberships, roles, room bans, invitations, contacts, blocks, and sessions SHALL be stored in a relational database and remain available across restarts and for years. Schema migrations SHALL be applied automatically on startup so a deployed instance is always at the expected schema version.

#### Scenario: Restart
- **WHEN** the backend restarts
- **THEN** all prior messages, memberships, and moderation state are intact

#### Scenario: Startup migration
- **WHEN** the application starts against a database at an older schema version
- **THEN** pending migrations are applied before serving traffic

### Requirement: File storage
Attachment content SHALL be stored on the local file system beneath a configurable uploads root, separate from the database, with a maximum of 20 MB per file and 3 MB per image.

#### Scenario: Configured root
- **WHEN** the uploads root is configured
- **THEN** uploaded content is written beneath it and served from there

### Requirement: Consistency of access decisions
Membership, room bans, user blocks, attachment access rights, message history, and admin/owner permissions SHALL be evaluated from the persisted state on every request, so a permission change takes effect on the next operation without waiting for a cache to expire or a client to refresh.

#### Scenario: Ban takes effect immediately
- **WHEN** a user is banned from a room
- **THEN** their very next attempt to read history, post, or download an attachment from that room is refused

#### Scenario: Promotion takes effect immediately
- **WHEN** a member is promoted to admin
- **THEN** their next moderation call in that room is authorized

### Requirement: Client-server transport boundary
The frontend SHALL reach the backend only through relative paths proxied by its own web server, covering the REST API, the identity endpoints, the real-time hub, and attachment content. No frontend code SHALL address the backend host directly.

#### Scenario: Proxied call
- **WHEN** the frontend calls a backend operation
- **THEN** it uses a relative path that the frontend's proxy forwards to the backend

### Requirement: Deployment topology
The system SHALL be deployable as three orchestrated services — a PostgreSQL database, the backend host, and the frontend web server — with the frontend the only service published to the browser and the database not exposed outside the internal network.

#### Scenario: Bringing the stack up
- **WHEN** the stack is started
- **THEN** the frontend is reachable in a browser, the backend is reachable only through the frontend's proxy, and the database is reachable only from within the internal network
