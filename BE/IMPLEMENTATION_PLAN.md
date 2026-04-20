# Back-End Implementation Plan — Classic Web Chat

## Context

Requirements from `2026_04_18_AI_herders_jam_-_requirements_v3 1.docx` describe a classic web chat: registration/login, public + private rooms, 1-to-1 personal messages, contacts/friends with user-to-user ban, file/image attachments, moderation (owner/admins), presence (online/AFK/offline), unread indicators, and persistent history with infinite scroll. Target scale is 300 concurrent users, rooms up to 1000 members, 10k+ messages per room, files up to 20 MB.

The `BE/` .NET 10 Clean Architecture skeleton (Domain/Application/Infrastructure/Web) already has ASP.NET Identity wired in with a single migration (Identity tables only) and `ApplicationUser : IdentityUser` with `DisplayName`. This plan extends the skeleton into a full chat back-end.

## Locked decisions

| Decision | Choice |
|---|---|
| Auth flow | **ASP.NET Core Identity API endpoints** (`MapIdentityApi<ApplicationUser>()`) — built-in register/login/refresh/reset endpoints with bearer tokens |
| Database | **PostgreSQL** via `Npgsql.EntityFrameworkCore.PostgreSQL`; existing SQL Server packages + migration replaced |
| XMPP/Jabber | **Out of scope** for v1; domain modelled generically so a later ejabberd sidecar is possible |
| Password reset email | **Dev stub** logs reset link via `IEmailSender`; `SmtpEmailSender` drop-in for prod behind `Email:Provider` flag; no SMTP in compose |
| Message pagination | **Keyset (cursor) pagination** — `?before={messageId}&limit=50`, indexed on `(RoomId, CreatedAt, Id)` |

## Cross-cutting decisions (with rationale)

- **Real-time transport — SignalR (single `ChatHub`).** Identity API bearer tokens accepted on the hub via the standard `access_token` query-string fallback for WebSockets. One hub keeps presence, message, typing, and moderation events in one connection.
- **File storage — local filesystem** under `/var/app/uploads/{roomId}/{attachmentId}` mounted as a docker volume. Downloads served by an authenticated `FilesController` that checks room/personal-chat membership on every request — **never** serve files as static content because access can be revoked.
- **Personal chats = special Room with `Kind = Direct`** and a deterministic two-member list. This lets messages, attachments, replies, editing, deletion share a single code path (requirement 2.5.1). User-to-user ban sets `IsFrozen = true`.
- **Presence — server-authoritative.** In-memory `ConcurrentDictionary<ConnectionId, {UserId, LastActivity}>`. A user is **online** if ≥1 connection had activity in the last 60 s, **AFK** if all connections idle >60 s, **offline** at connection count 0. FE emits a lightweight `Ping(active)` every 20 s per tab (with `BroadcastChannel` cross-tab dedup).
- **Unread counters** — per `(UserId, RoomId)` tracks `LastReadMessageId`. Count = rows where `Message.Id > LastReadMessageId`. Cleared on room open.
- **Validation** — FluentValidation in the Application layer; errors projected to RFC 7807 `ProblemDetails`.
- **Mediator/CQRS** — MediatR for commands/queries. Thin controllers; SignalR hub methods reuse the same handlers.
- **Mapping** — hand-written projections in query handlers; add Mapster only if duplication becomes painful.

## Existing vs. new

Keep: Clean Architecture layout, `ApplicationUser`, `ApplicationDbContext`, Dockerfile, Program.cs scaffold, Identity password/lockout config.

Replace:
- `Microsoft.EntityFrameworkCore.SqlServer` → `Npgsql.EntityFrameworkCore.PostgreSQL` (Infrastructure + Web csprojs).
- Custom `AccountController` register/login/logout → `app.MapIdentityApi<ApplicationUser>()`. A new `MeController` keeps profile, display-name change, and account deletion.
- Existing initial migration — **delete** and regenerate against Postgres covering Identity + all chat tables.

Add packages:
- `Application`: `MediatR`, `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`.
- `Infrastructure`: `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `Web`: `Microsoft.AspNetCore.SignalR` (built-in but confirm reference).

## Domain entities (`BE/Domain/Entities/`)

- `ApplicationUser` (exists) — add `CreatedAt`, `DeletedAt` (soft delete for audit of owned rooms). `UserName` stays the immutable unique username (requirement 2.1.2).
- `Room` — `Id`, `Name` (unique for Group rooms), `Description`, `Visibility` (Public/Private), `Kind` (Group/Direct), `OwnerId?`, `IsFrozen`, `CreatedAt`, `DeletedAt?`.
- `RoomMembership` — `RoomId`, `UserId`, `Role` (Owner/Admin/Member), `JoinedAt`, `LastReadMessageId?`. Composite PK (`RoomId`, `UserId`).
- `RoomBan` — `RoomId`, `BannedUserId`, `BannedByUserId`, `Reason?`, `CreatedAt`. Composite PK (`RoomId`, `BannedUserId`).
- `RoomInvitation` — `Id`, `RoomId`, `InvitedUserId`, `InvitedByUserId`, `Status` (Pending/Accepted/Rejected), `CreatedAt`.
- `Message` — `Id` (Guid v7 for sortability), `RoomId`, `AuthorId`, `Text`, `ReplyToMessageId?`, `EditedAt?`, `DeletedAt?`, `CreatedAt`.
- `Attachment` — `Id`, `MessageId`, `FileName` (original), `StoragePath`, `ContentType`, `SizeBytes`, `Kind` (Image/File), `Comment?`, `CreatedAt`.
- `Friendship` — `UserAId`, `UserBId` (canonically ordered `A < B`), `Status` (Pending/Accepted), `RequestedByUserId`, `Message?`, `CreatedAt`, `AcceptedAt?`. Composite PK (`UserAId`, `UserBId`) + check constraint.
- `UserBlock` — `BlockerId`, `BlockedId`, `CreatedAt`. Composite PK.

Enums: `RoomVisibility`, `RoomKind`, `RoomRole`, `FriendshipStatus`, `AttachmentKind`, `InvitationStatus`.

## Application layer (`BE/Application/`)

Folders:
- `Abstractions/` — interfaces: `IApplicationDbContext`, `IEmailSender`, `IFileStorage`, `IPresenceTracker`, `IChatNotifier`, `ICurrentUser`.
- `Common/` — `Result<T>`, `Paged<T>`, `CursorPaged<T>`, error types.
- `Features/` — one folder per aggregate, each with `Commands/`, `Queries/`, `Validators/`, `Dtos/`.

Feature folders:
- `Rooms` — Create, Update, Delete, Join, Leave, ListPublic (search), Get, ListMyRooms, Invite, AcceptInvite, Ban, Unban, ListBanned, MakeAdmin, RemoveAdmin, RemoveMember.
- `Messages` — Send, Edit, Delete, GetHistory (keyset), GetOne, MarkRead.
- `Attachments` — Upload, GetMetadata, Download (auth-checked stream).
- `Friends` — Request, Accept, Reject, Remove, List, Block, Unblock, ListBlocks.
- `DirectChats` — OpenOrCreate (by username) — reuses Room with `Kind=Direct`.
- `Profile` — GetMe, UpdateDisplayName, DeleteAccount.
- `Sessions` — ListMySessions, RevokeSession.

Validators enforce: message ≤ 3 KB UTF-8, file ≤ 20 MB, image ≤ 3 MB, username/room-name uniqueness (pre-check + DB unique index as final guard), emails unique, immutable `UserName`.

## Infrastructure (`BE/Infrastructure/`)

- `Data/ApplicationDbContext.cs` — add `DbSet<>` for every entity. Configure:
  - Composite keys (`RoomMembership`, `Friendship`, `UserBlock`, `RoomBan`).
  - Unique indexes (`Room.Name` filtered on `Kind=Group`, `ApplicationUser.UserName` already by Identity).
  - Covering index `(RoomId, CreatedAt DESC, Id)` on `Message`.
  - Check constraint `Friendship.UserAId < Friendship.UserBId`.
  - Cascading deletes Room→Messages→Attachments, Room→Memberships, Room→Bans, Room→Invitations.
- `Data/Migrations/` — **delete** existing; regenerate one `InitialPostgres` migration covering Identity + all chat tables.
- `Services/LocalFileStorage.cs` — writes to `UploadsRoot` from config, returns relative storage path. Streams on read.
- `Services/DevEmailSender.cs` — logs reset link to ILogger.
- `Services/SmtpEmailSender.cs` — MailKit-based; selected when `Email:Provider=Smtp`.
- `Services/InMemoryPresenceTracker.cs` — `ConcurrentDictionary`; emits `PresenceChanged` via `IChatNotifier`. Adequate for 300 users on a single instance. Future: Redis-backed tracker + SignalR Redis backplane for horizontal scale.
- `Identity/IdentityEmailSender.cs` — adapter so Identity API uses our `IEmailSender`.
- `DependencyInjection.cs` — extension method `AddInfrastructure(IConfiguration)` registers DbContext, services, Identity email sender.

## Web (`BE/Web/`)

- `Program.cs` additions:
  - `AddDbContext<ApplicationDbContext>(UseNpgsql(...))`.
  - `AddIdentityApiEndpoints<ApplicationUser>()` with options, `.AddEntityFrameworkStores<ApplicationDbContext>()`.
  - `AddMediatR`, `AddValidatorsFromAssembly`, `AddSignalR`, `AddProblemDetails`, `AddCors` (FE origin from config).
  - Configure bearer token handler `OnMessageReceived` to accept `?access_token=` when path starts with `/hubs/`.
  - `MapIdentityApi<ApplicationUser>()` under `/auth`.
  - `MapControllers()`, `MapHub<ChatHub>("/hubs/chat")`.
  - Startup migration (keep existing `context.Database.MigrateAsync()` block).
- `Controllers/` — thin, one per aggregate dispatching to MediatR:
  - `MeController`, `RoomsController`, `MessagesController`, `AttachmentsController`, `FilesController` (streaming download with auth check), `FriendsController`, `DirectChatsController`, `SessionsController`, `InvitationsController`.
- `Hubs/ChatHub.cs` — methods: `SendMessage`, `EditMessage`, `DeleteMessage`, `StartTyping`, `StopTyping`, `Ping(bool active)`, `MarkRead`. Uses groups `room:{id}` and `user:{id}`. On connect: add to groups for every room the user belongs to + `user:{id}`.
- `Hubs/ChatNotifier.cs` — `IChatNotifier` impl; lets Application layer push events without referencing SignalR.
  - Events: `MessageReceived`, `MessageEdited`, `MessageDeleted`, `PresenceChanged`, `RoomMembershipChanged`, `UnreadUpdated`, `FriendRequestReceived`, `RoomDeleted`.
- `Configuration/` — `UploadOptions`, `EmailOptions`, `CorsOptions` bound from `IConfiguration`.
- `appsettings.json` — Postgres connection string, uploads root, email provider, allowed CORS origins.

## REST surface

Identity API (mounted at `/auth`): register, login, refresh, forgotPassword, resetPassword, manage/info, etc.

Domain REST (under `/api`):

- `GET /me`, `PATCH /me`, `DELETE /me`
- `GET /sessions`, `DELETE /sessions/{id}`
- `GET /rooms/public?search=&cursor=`, `POST /rooms`, `GET /rooms/mine`, `GET /rooms/{id}`, `PATCH /rooms/{id}`, `DELETE /rooms/{id}`
- `POST /rooms/{id}/join`, `POST /rooms/{id}/leave`
- `GET /rooms/{id}/members`, `DELETE /rooms/{id}/members/{userId}`, `POST /rooms/{id}/members/{userId}/ban`, `POST /rooms/{id}/members/{userId}/admin`, `DELETE /rooms/{id}/members/{userId}/admin`
- `GET /rooms/{id}/bans`, `DELETE /rooms/{id}/bans/{userId}`
- `POST /rooms/{id}/invitations`, `GET /me/invitations`, `POST /invitations/{id}/accept`, `POST /invitations/{id}/reject`
- `GET /rooms/{id}/messages?before=&limit=`, `POST /rooms/{id}/messages`, `PATCH /messages/{id}`, `DELETE /messages/{id}`, `POST /rooms/{id}/read`
- `POST /attachments` (multipart), `GET /attachments/{id}` (metadata), `GET /files/{attachmentId}` (auth-checked download)
- `GET /friends`, `POST /friends/requests`, `POST /friends/requests/{id}/accept`, `POST /friends/requests/{id}/reject`, `DELETE /friends/{userId}`
- `POST /blocks/{userId}`, `DELETE /blocks/{userId}`, `GET /blocks`
- `POST /direct-chats` (body: `{ username }`) → returns the Room with `Kind=Direct`.

## Docker

- `BE/Dockerfile` — retain multi-stage `sdk:10.0` → `aspnet:10.0` final. Expose 8080.
- Root `docker-compose.yml` (not part of BE folder but tightly coupled):
  - `db`: `postgres:16-alpine`, volume `pgdata`, env `POSTGRES_PASSWORD`/`POSTGRES_DB=andreychat`.
  - `api`: build `./BE`, env `ConnectionStrings__DefaultConnection`, volume `uploads:/var/app/uploads`, depends_on `db`.
  - `web`: built from `./FE` (nginx), proxies `/api`, `/auth`, `/hubs`, `/files` to `api:8080`. Exposes port 80.

## Testing

- xUnit projects under `BE/tests/`:
  - `Application.Tests` — handler tests with in-memory Postgres via Testcontainers.
  - `Web.Tests` — `WebApplicationFactory` end-to-end:
    - register → login → create room → send message → read back
    - permission matrix: owner/admin/member actions for ban, kick, delete message, promote/demote
    - attachment auth (member can download, banned user cannot)
    - friend + block flow freezes direct chat
    - keyset pagination returns stable pages across concurrent inserts
- Not targeting 100% coverage; prioritise auth, permissions, messaging, attachment auth.

## Critical files to create or modify

Modify:
- `BE/Infrastructure/Infrastructure.csproj` (swap provider)
- `BE/Web/Web.csproj` (swap provider, add SignalR if needed)
- `BE/Application/Application.csproj` (add MediatR + FluentValidation)
- `BE/Web/Program.cs` (swap AccountController flow for Identity API + hub mapping + CORS + bearer-on-hub)
- `BE/Web/appsettings.json` (Postgres, uploads, email, CORS)
- `BE/Infrastructure/Data/ApplicationDbContext.cs` (DbSets + Fluent config)
- `BE/Domain/Entities/ApplicationUser.cs` (CreatedAt, DeletedAt)

Delete:
- `BE/Infrastructure/Data/Migrations/*` (regenerate on Postgres)
- `BE/Web/Controllers/AccountController.cs` (superseded by Identity API + MeController)

Add:
- All `Domain/Entities/*.cs` listed above and their enums.
- All `Application/Features/**/*.cs` listed above + `Application/Abstractions/*.cs`, `Application/Common/*.cs`, `Application/DependencyInjection.cs`.
- `Infrastructure/Services/*.cs`, `Infrastructure/Identity/*.cs`, `Infrastructure/DependencyInjection.cs`, regenerated migration.
- `Web/Controllers/*.cs` (list above), `Web/Hubs/ChatHub.cs`, `Web/Hubs/ChatNotifier.cs`, `Web/Configuration/*.cs`.
- `BE/tests/Application.Tests/`, `BE/tests/Web.Tests/` projects and add to `BE.slnx`.

## Verification

1. `dotnet build BE/BE.slnx` — no errors, no warnings treated as errors.
2. `docker compose up --build db api` — API applies `InitialPostgres` migration, logs `Application started`.
3. `dotnet test` — green.
4. Smoke via Swagger at `/swagger`:
   - `POST /auth/register`, `POST /auth/login`, capture access token.
   - `POST /api/rooms` (Group/Public), `GET /api/rooms/public` — new room visible.
   - `POST /api/rooms/{id}/messages`, `GET /api/rooms/{id}/messages?before=` — keyset pagination works.
   - `POST /api/attachments` with 2 MB image, `GET /files/{id}` — streams back.
   - Second user `POST /api/rooms/{id}/join`, send message, first user reads it.
   - Admin bans second user, second user `GET /files/{id}` → 403.
5. SignalR smoke (via FE or a minimal JS client): connect with `?access_token=`, subscribe, verify `MessageReceived` and `PresenceChanged` events.

## Out of scope (v1)

- XMPP/Jabber federation, admin dashboard, load test.
- Horizontal scaling of SignalR (note Redis backplane as future work).
- Email confirmation on register (requirement 2.1.2 says not required).
- SMTP configured in compose (adapter exists but no SMTP container).
- Push notifications beyond in-app unread indicators.
