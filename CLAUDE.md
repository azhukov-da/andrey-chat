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

Then **test** — see [Automated tests](#automated-tests) below for the full picture:
- One command: `test.bat` runs all three gated layers plus both coverage reports.
- Manual: open `http://localhost:3000`, sign up / sign in, exercise the affected flow.
- Backend builds during the docker build of the `be` service; for local dev, see [BE/CLAUDE.md](BE/CLAUDE.md).

Service URLs once up:

- Frontend: `http://localhost:3000`
- Backend (inside Docker network): `http://be:8080` — proxied by the FE nginx at `/api`, `/hubs`, `/login`, `/register`, `/refresh`, `/manage`, `/uploads` (see [FE/nginx.conf](FE/nginx.conf))
- Postgres: container `db` on `5432` (no host port published; `docker-compose.tests.yml` adds `55432` on the host for the test suite)

> **Known collision:** because nginx proxies `/login` and `/register` to the backend's Identity endpoints, the SPA's own sign-in and registration screens 405 on direct navigation or a page refresh. They are only reachable by client-side routing from `/`. The e2e fixtures work around this — see [FE/e2e/fixtures.ts](FE/e2e/fixtures.ts).

## Automated tests

Behaviour is verified by three **gated layers**, each accountable for a different class of specified behaviour. The requirements for all of this live in the `automated-testing` capability — currently the delta spec at [openspec/changes/add-test-harness/specs/automated-testing/spec.md](openspec/changes/add-test-harness/specs/automated-testing/spec.md), which moves to `openspec/specs/automated-testing/spec.md` when that change is archived.

| Layer | Where | Verifies | Run it |
|---|---|---|---|
| Backend integration | [BE/Tests.Integration/](BE/Tests.Integration/) | Server-enforced rules, through real HTTP and a real SignalR connection against a real PostgreSQL database with production migrations applied | `dotnet test BE/Tests.Integration/Tests.Integration.csproj` |
| Frontend unit / component | [FE/src/](FE/src/) (`*.test.tsx`) | Client-enforced rules and rendering, through the component tree with the backend transports substituted | `cd FE && npm test` |
| End-to-end | [FE/e2e/](FE/e2e/) | Cross-cutting flows only the assembled system can show, in a real browser against the running stack | `cd FE && npm run e2e` |

A fourth layer, load and capacity, is specified but not yet built; it is deliberately outside the gated run.

### Prerequisites per layer

| Layer | Needs |
|---|---|
| Backend integration | PostgreSQL on `localhost:55432` — `docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db`. `test.bat` starts this container itself if it is not already up. |
| Frontend unit | Nothing running. `cd FE && npm install` is enough. |
| End-to-end | The whole stack serving on `http://localhost:3000` — `start.bat` |

Each layer runs on its own without the others' prerequisites, and each fails fast with a message naming what to start. The port `55432` lives only in `docker-compose.tests.yml` so the base compose file keeps the database off the host, which is what the `platform-constraints` spec requires of the deployed topology.

### Test data isolation

The backend integration layer creates a database named `chat_test_<guid>` per xUnit collection, migrates it through the application's own startup path, truncates it between tests with Respawn, and drops it at the end. It never opens a connection to the development `chat` database. A run interrupted before cleanup leaves at most one database behind, and the next run reclaims anything older than 24 hours.

The end-to-end layer is the exception, by design: it drives the developer's own stack and adds real accounts and rooms to the development database. Its users get generated identifiers so runs never collide; `docker compose down -v` resets things when the clutter matters.

### Reports

`test.bat` writes everything under `docs/test-coverage/`. Only `scenarios.md` is committed — it is the artefact worth reading in a diff, because it shows which specified scenarios gained or lost a test.

| Report | Path |
|---|---|
| Scenario coverage (committed) | `docs/test-coverage/scenarios.md` |
| Backend line coverage | `docs/test-coverage/backend/index.html` |
| Frontend line coverage | `docs/test-coverage/frontend/index.html` |
| End-to-end run | `docs/test-coverage/e2e-report/index.html` |
| Raw machine-readable results | `docs/test-coverage/raw/` |

### The two gates

They measure different things and are tracked separately.

- **Scenario coverage** — [tools/spec-coverage](tools/spec-coverage/) parses `openspec/specs/**/spec.md` into scenario identifiers and cross-references them against the tests that claim them. It says *what is left to test*. It does not gate on percentage, but it **does** fail the run when a test claims an identifier no scenario matches, which is how a renamed or misspelled scenario surfaces.
- **Line coverage** — [tools/coverage-gate](tools/coverage-gate/) compares backend and frontend line coverage against 80%. It currently **reports** the shortfall without failing; `test.bat` calls it without `--enforce` because only `user-sessions` has tests so far, and failing on coverage would bury real test failures. Turning the gate on is adding that one flag.

### Declaring what a test verifies

Every test names the scenarios it verifies by putting `@spec:<capability>/<requirement-slug>/<scenario-slug>` in its **display name**. The identifier is derived from the spec headings — lowercased, non-alphanumerics collapsed to dashes — so it cannot drift from the specs. See [docs/testing-conventions.md](docs/testing-conventions.md) for the per-layer syntax.

Only a **passing** test counts as covering its claims; a failing or skipped one is reported as claimed-but-not-passing.

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
