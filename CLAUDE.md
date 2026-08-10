# Andrey Chat

A real-time chat application with rooms, direct messages, friends, presence, and attachments. Composed of a .NET 10 backend ([BE/](BE/)) and a React 19 + Vite frontend ([FE/](FE/)), backed by PostgreSQL. Orchestrated via Docker Compose.

See [BE/CLAUDE.md](BE/CLAUDE.md) and [FE/CLAUDE.md](FE/CLAUDE.md) for stack-specific guidance.

## Repository layout

- [BE/](BE/) — .NET 10 solution (Clean Architecture: Domain, Application, Infrastructure, Web)
- [FE/](FE/) — React 19 + Vite + TypeScript SPA
- [docker-compose.yml](docker-compose.yml) — `db` (postgres:17-alpine), `be`, `fe`
- [start.bat](start.bat) — one-shot build + run + open-browser
- [chat_requirements.md](chat_requirements.md), [TheStory.md](TheStory.md), [wireframes.txt](wireframes.txt) — product spec
- [analyzer/](analyzer/) — automated requirement/wireframe audit tooling

## Restart and test the entire project

To restart and wait for readiness, use the `restart` agent (see [.claude/agents/restart.md](.claude/agents/restart.md)) — it runs `start.bat` and polls `http://localhost:3000` on the escalating 10s → 30s → 60s → 120s schedule.

Then **test**:
- Manual: open `http://localhost:3000`, sign up / sign in, exercise the affected flow.
- Programmatic: drive the live site with Playwright (the `analyze` and `fix` agents do this against `http://localhost:3000`).
- Frontend unit tests: `cd FE && npm test` (vitest).
- Backend builds during the docker build of the `be` service; for local dev, see [BE/CLAUDE.md](BE/CLAUDE.md).

Service URLs once up:

- Frontend: `http://localhost:3000`
- Backend (inside Docker network): `http://be:8080` — proxied by the FE nginx at `/api`, `/hubs`, `/login`, `/register`, `/refresh`, `/manage`, `/uploads` (see [FE/nginx.conf](FE/nginx.conf))
- Postgres: container `db` on `5432` (no host port published)

## Frontend ↔ Backend interface

The frontend talks to the backend over **two** channels: HTTP (REST + Identity) and a SignalR hub. Both are exposed on the same backend host and proxied through nginx.

### 1. HTTP / REST — Swagger

The backend uses **Swashbuckle**. Swagger UI is enabled only when `ASPNETCORE_ENVIRONMENT=Development`:

- Swagger UI: `http://localhost:8080/swagger` (run BE locally with `dotnet run --project BE/Web` to access; in Docker the env is `Production` and Swagger is disabled)
- OpenAPI doc: `http://localhost:8080/swagger/v1/swagger.json`

Controllers ([BE/Web/Controllers/](BE/Web/Controllers/)):

- `AuthController` — extra auth flows on top of Identity
- `MeController` — current user profile
- `RoomsController` — group chat rooms / memberships
- `DirectChatsController` — 1:1 chats
- `MessagesController` — message history (paged)
- `FriendsController` — friend requests / list
- `InvitationsController` — room invitations
- `AttachmentsController` — file uploads
- `SessionsController` — active sessions / device list

Identity endpoints are mapped via `MapIdentityApi<ApplicationUser>()` ([BE/Web/Program.cs:100](BE/Web/Program.cs#L100)) and tagged `Auth` in Swagger. They include `/login`, `/register`, `/refresh`, `/manage/*` (2FA/info endpoints are hidden from Swagger). The FE consumes them through [FE/src/api/](FE/src/api/) — one module per resource, using the shared `apiFetch`/`apiJson` helpers in [FE/src/api/client.ts](FE/src/api/client.ts) which handle Bearer token injection, `X-Session-Id` header, and automatic 401 refresh via `/refresh`.

Auth model: JWT bearer access token + refresh token, both stored client-side in `useAuthStore` ([FE/src/stores/authStore.ts](FE/src/stores/authStore.ts)).

### 2. SignalR — real-time hub

**Endpoint:** `/hubs/chat` (mapped at [BE/Web/Program.cs:101](BE/Web/Program.cs#L101); hub: [BE/Web/Hubs/ChatHub.cs](BE/Web/Hubs/ChatHub.cs); server-push notifier: [BE/Web/Hubs/ChatNotifier.cs](BE/Web/Hubs/ChatNotifier.cs)).

**Auth:** `[Authorize]` on the hub — requires the same JWT as REST. The FE supplies it via `accessTokenFactory` in [FE/src/realtime/hubClient.ts](FE/src/realtime/hubClient.ts).

**Reconnect policy:** `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])`.

**Groups** (joined automatically by `ChatNotifier.EnrollUserGroupsAsync` on connect):
- `user:{userId}` — personal notifications (unread, friend requests)
- `room:{roomId}` — per-room messages, membership changes, deletions

**Client → Server (invocations on `ChatHub`):**

| Method | Args | Purpose |
|---|---|---|
| `Ping` | `bool active` | Presence heartbeat (online/AFK) |
| `GetPresenceFor` | `string[] userIds` | Bulk presence lookup, returns `Dictionary<string,string>` |
| `SendMessage` | `Guid roomId, string text, Guid? replyToMessageId` | Send a message (server fans out `MessageReceived`) |
| `EditMessage` | `Guid messageId, string text` | Edit own message |
| `DeleteMessage` | `Guid messageId` | Soft-delete own message |
| `MarkRead` | `Guid roomId, Guid messageId` | Mark room read up to message |
| `StartTyping` | `Guid roomId` | Broadcasts `UserTyping` to others in room |
| `StopTyping` | `Guid roomId` | Broadcasts `UserStoppedTyping` |

Errors surface as `HubException` with the failing service's error message.

**Server → Client events** (registered FE-side in [FE/src/realtime/events.ts](FE/src/realtime/events.ts)):

| Event | Payload | Effect |
|---|---|---|
| `MessageReceived` | `Message` DTO | Prepended to `['messages', roomId]` cache; bumps unread when room not active |
| `MessageEdited` | `{ messageId, newText, editedAt }` | Updates message in cache |
| `MessageDeleted` | `{ messageId }` | Marks message `isDeleted`, clears text |
| `PresenceChanged` | `{ userId, status }` (`online`/`afk`/`offline`) | Updates `presenceStore` |
| `RoomMembershipChanged` | `{ roomId, userId, action }` | Invalidates room/membership queries |
| `RoomDeleted` | `{ roomId }` | Invalidates rooms; navigates away if active room was deleted |
| `FriendRequestReceived` | friend-request DTO | Invalidates `['friends']` |
| `UnreadUpdated` | `{ roomId, unreadCount }` | Sets unread count in `unreadStore` |
| `UserTyping` / `UserStoppedTyping` | `{ userId, roomId }` | Typing indicators |

**Lifecycle on the FE:** `useSignalR` hook starts the connection after login and registers/unregisters the events above. A `presencePing` loop ([FE/src/realtime/presencePing.ts](FE/src/realtime/presencePing.ts)) periodically invokes `Ping`.

## Conventions

- The FE proxies all backend traffic through its nginx (in Docker) or Vite dev proxy — never call `http://localhost:8080` directly from FE code; use relative paths.
- Stick to the single `apiFetch`/`apiJson` client for REST calls so token refresh and session headers are uniform.
- Never call `hub.start()` ad-hoc — go through `startHub()`/`stopHub()` so there's a single connection.
- Keep wireframe/requirement parity by checking [chat_requirements.md](chat_requirements.md), [TheStory.md](TheStory.md), [wireframes.txt](wireframes.txt) before changing UI behavior.
