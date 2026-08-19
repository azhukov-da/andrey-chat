# Backend — Andrey Chat

.NET 10 (`net10.0`) Clean Architecture solution. Postgres via EF Core. Real-time via SignalR. ASP.NET Core Identity for auth.

> See the root [CLAUDE.md](../CLAUDE.md) for the FE↔BE contract (REST endpoints + SignalR hub) and the project-wide restart/test workflow.

## Solution layout

- [Domain/](Domain/) — entities ([Entities/](Domain/Entities/)) and enums ([Enums/](Domain/Enums/)). No framework dependencies.
- [Application/](Application/) — use-case layer.
  - [Abstractions/](Application/Abstractions/) — interfaces consumed by Web/Infrastructure (`IMessageService`, `IPresenceTracker`, `IChatNotifier`, `ICurrentUser`, `IApplicationDbContext`, …).
  - [Features/](Application/Features/) — feature folders: `Auth`, `DirectChats`, `Friends`, `Messages`, `Profile`, `Rooms`, `Sessions`. Validators registered via `AddValidatorsFromAssembly`.
  - [Services/](Application/Services/) — concrete services wired in [DependencyInjection.cs](Application/DependencyInjection.cs) (`ProfileService`, `RoomService`, `MessageService`, `DirectChatService`, `FriendService`, `SessionService`).
  - [Common/](Application/Common/) — shared `Result`/`Error` types.
- [Infrastructure/](Infrastructure/) — `ApplicationDbContext`, EF migrations ([Migrations/](Infrastructure/Migrations/)), Identity wiring, presence tracker, attachment storage, etc.
- [Tests.Integration/](Tests.Integration/) — xUnit v3 integration suite. Hosts `Web` in-process via `WebApplicationFactory<Program>` against a real PostgreSQL database.
- [Web/](Web/) — ASP.NET Core host.
  - [Controllers/](Web/Controllers/) — REST surface (see root CLAUDE.md for the list).
  - [Hubs/](Web/Hubs/) — `ChatHub` (client invocations) and `ChatNotifier` (server-side push, registered as singleton `IChatNotifier`).
  - [Configuration/](Web/Configuration/) — typed options (e.g. `CorsOptions`).
  - [Program.cs](Web/Program.cs) — composition root.

## Composition (Program.cs)

`AddApplication()` + `AddInfrastructure(Configuration)` register the DI graph. `AddIdentityApiEndpoints<ApplicationUser>()` is mapped via `MapIdentityApi` and tagged `Auth` in Swagger. `ChatNotifier` is registered as **singleton** because it captures `IHubContext<ChatHub>`; it resolves scoped services (`IApplicationDbContext`) via an injected `IServiceProvider` + `CreateScope` — preserve that pattern when adding work that touches the DB.

DB migrations run automatically on startup via `dbContext.Database.MigrateAsync()`. Do not bypass — add a new migration instead of editing existing ones.

## Auth model

- ASP.NET Core Identity (cookie-less). Identity API endpoints provide `/login`, `/register`, `/refresh`, `/manage/*`. The Swagger filter in [Program.cs](Web/Program.cs) hides `manage/2fa` and `manage/info`.
- Bearer JWT on REST, same JWT on SignalR via query string (`accessTokenFactory` on the FE).
- `ICurrentUser` abstracts the authenticated user inside Application services.

## SignalR

- Hub: [Web/Hubs/ChatHub.cs](Web/Hubs/ChatHub.cs) — `[Authorize]`, exposes `Ping`, `GetPresenceFor`, `SendMessage`, `EditMessage`, `DeleteMessage`, `MarkRead`, `StartTyping`, `StopTyping`.
- Server push: [Web/Hubs/ChatNotifier.cs](Web/Hubs/ChatNotifier.cs) — fans out via groups `user:{userId}` and `room:{roomId}`. Service-layer code should call `IChatNotifier`, never `IHubContext` directly.
- On connect: `EnrollUserGroupsAsync` adds the connection to its `user:` group and every `room:` group it belongs to (one DB query against `RoomMemberships`).
- Service-method failures should be surfaced as `Result.Failure(...)`; the hub turns them into `HubException` with the error message.
- Detailed reference: [docs/SIGNALR.md](docs/SIGNALR.md), [docs/SIGNALR_QUICK_REFERENCE.md](docs/SIGNALR_QUICK_REFERENCE.md).

## CORS

`CorsOptions:AllowedOrigins` (env var `Cors__AllowedOrigins__0` etc.). Default policy allows credentials, any header, any method — required for SignalR with JWT.

## Configuration

- [Web/appsettings.json](Web/appsettings.json) / [appsettings.Development.json](Web/appsettings.Development.json).
- Connection string env var: `ConnectionStrings__DefaultConnection`.
- Logging via Serilog (console sink only).

## Local dev

```
dotnet run --project BE/Web
```

Runs against the connection string in `appsettings.Development.json`. Swagger UI is then at `http://localhost:8080/swagger` (or whatever Kestrel binds). For the full stack, prefer the root-level `start.bat` (see root CLAUDE.md).

## Integration tests

```
docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db   # once
dotnet test BE/Tests.Integration/Tests.Integration.csproj
```

The suite needs PostgreSQL on `localhost:55432` and nothing else — no `be` container, no frontend.
It fails immediately with the command to start it if the database is unreachable. Override the
endpoint with `TEST_DB_HOST` / `TEST_DB_PORT` / `TEST_DB_USER` / `TEST_DB_PASSWORD`.

The harness lives in [Tests.Integration/Harness/](Tests.Integration/Harness/):

- `ChatAppFixture` — one `chat_test_<guid>` database and one host per xUnit collection. Migrations run through `Program.cs`'s own startup path, so the schema is whatever the application would create. Respawn truncates between tests.
- `ChatAppFactory` — the `WebApplicationFactory`. Runs in the `Testing` environment so `appsettings.Development.json` is not inherited. Host construction is serialised behind a semaphore because `Program.cs` reads the connection string eagerly, before any `ConfigureWebHost` source is visible; the only channel that lands early enough is the process environment, which is global.
- `AuthHelper` / `TestUser` — registers, signs in, and session-registers a user, returning an `HttpClient` preloaded with `Authorization` and `X-Session-Id`.
- `TestHubClient` — an authenticated `HubConnection` over the `TestServer`. **Uses the WebSocket transport, not long polling, and that is load-bearing:** hub methods reach the caller's identity through `ICurrentUser`, which reads the ambient `IHttpContextAccessor.HttpContext`. On WebSockets the connection lives inside one long-running request so that context is still ambient; on long polling each poll is its own request and every hub method fails as unauthenticated.
- `IntegrationTest` — the base class. Resets the database before each test and exposes `CreateUserAsync` / `ConnectHubAsync`.

Two things the harness has to stand in for the transport on: `TestServer` leaves
`Connection.RemoteIpAddress` unset, so a test that asserts on the recorded IP builds its request
through `App.Server.SendAsync` (see `UserSessions/SessionsTests.cs`).

`Program.cs` has two test-only accommodations, both commented in place: the
`public partial class Program` marker the factory needs, and an exception filter on the catch-all so
the host-shutdown signal the factory uses is not swallowed.

Coverage exclusions are declared in [coverlet.runsettings](coverlet.runsettings). Scenario claim
syntax is in [docs/testing-conventions.md](../docs/testing-conventions.md).

## Adding a feature

1. Add entity + enum in `Domain` if needed.
2. Add EF migration: `dotnet ef migrations add <Name> --project Infrastructure --startup-project Web`.
3. Define abstraction in `Application/Abstractions`, implement in `Application/Services`, register in `Application/DependencyInjection.cs`.
4. Add validators in `Application/Features/<Area>` — they auto-register.
5. Expose via a controller in `Web/Controllers/` and/or hub method in `Web/Hubs/ChatHub.cs`.
6. If real-time, add a fan-out method to `IChatNotifier` + `ChatNotifier` and document the event payload in the root CLAUDE.md SignalR table.

## Conventions

- Return `Result<T>` from Application services; hubs and controllers translate failures uniformly.
- Never reach across layer boundaries: `Web` doesn't reference `Infrastructure` types directly except for the DI extension and the `ApplicationDbContext` migration call in `Program.cs`.
- Group keys are stable strings: `user:{userId}` and `room:{roomId}` — don't invent new prefixes without updating both sides and `EnrollUserGroupsAsync`.
- `ChatNotifier` is a **singleton**; capture only singletons or resolve scoped services through `IServiceProvider`.
