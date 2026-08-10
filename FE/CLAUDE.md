# Frontend — Andrey Chat

React 19 + TypeScript + Vite SPA. State via Zustand + TanStack Query. Routing via `react-router` v7. Realtime via `@microsoft/signalr`. Styling via Tailwind + DaisyUI.

> See the root [CLAUDE.md](../CLAUDE.md) for the full FE↔BE contract (REST + SignalR) and the project-wide restart/test workflow.

## Source layout

- [src/api/](src/api/) — one module per backend resource (`auth`, `me`, `rooms`, `directChats`, `messages`, `friends`, `sessions`, `attachments`). All HTTP goes through [src/api/client.ts](src/api/client.ts) (`apiFetch`/`apiJson`/`ApiError`).
- [src/realtime/](src/realtime/) — SignalR.
  - [hubClient.ts](src/realtime/hubClient.ts) — singleton `HubConnection`. Use `getHubConnection()`, `startHub()`, `stopHub()`. Don't call `hub.start()` elsewhere.
  - [events.ts](src/realtime/events.ts) — `registerHubEvents(hub, queryClient, navigate)` wires every server-push event to the React Query cache and Zustand stores. Returns an unregister function.
  - [presencePing.ts](src/realtime/presencePing.ts) — periodic `Ping(active)` heartbeat.
- [src/stores/](src/stores/) — Zustand: `authStore` (tokens), `presenceStore`, `unreadStore`, `uiStore` (active room id, etc.).
- [src/features/](src/features/) — feature folders: `auth`, `chat`, `friends`, `layout`, `profile`, `rooms`, `sessions`. Components colocated with hooks and small utilities.
- [src/hooks/](src/hooks/) — cross-feature hooks (`useSignalR`, etc.).
- [src/types/](src/types/) — shared DTO types mirroring backend payloads.
- [src/lib/](src/lib/) — small utilities (formatting, etc.).
- [src/routes.tsx](src/routes.tsx) — route table.
- [src/App.tsx](src/App.tsx) / [src/main.tsx](src/main.tsx) — composition root.
- [vite.config.ts](vite.config.ts) — `@` → `src`, dev proxy targets `https://localhost:7071` (BE local Kestrel).
- [nginx.conf](nginx.conf) — production proxy used by the docker image (forwards `/api`, `/hubs`, auth/identity paths, `/uploads` to the `be` container).

## Backend communication

**REST.** Always use `apiJson<T>` / `apiFetch` from [src/api/client.ts](src/api/client.ts). They:

- inject `Authorization: Bearer <accessToken>` from `useAuthStore`,
- attach `X-Session-Id` from `localStorage` if present,
- transparently refresh on `401` via `POST /refresh`, with a single in-flight refresh,
- throw `ApiError(status, message)` with a flattened ProblemDetails/validation message.

Never call `fetch` directly for backend resources; you'll bypass token handling and 401 retry.

**SignalR.** See [hubClient.ts](src/realtime/hubClient.ts). The connection sends the JWT via `accessTokenFactory` and uses `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])`. Server-push events are listed (with payloads and side-effects) in the root [CLAUDE.md](../CLAUDE.md). Client invocations: `Ping`, `GetPresenceFor`, `SendMessage`, `EditMessage`, `DeleteMessage`, `MarkRead`, `StartTyping`, `StopTyping`.

When a server event needs to update the UI:

1. Add the handler in [src/realtime/events.ts](src/realtime/events.ts) (single source of truth).
2. Update `queryClient` cache via `setQueryData`/`setQueriesData` for known shapes; fall back to `invalidateQueries` only when the cache shape is non-trivial.
3. Update Zustand stores (`presenceStore`, `unreadStore`, `uiStore`) for derived state.
4. Don't subscribe ad-hoc to `hub.on` from components — events.ts already centralises lifecycle.

## State

- **Server state** → TanStack Query. Query keys are tuples like `['messages', roomId]`, `['rooms', 'mine']`, `['rooms', roomId, 'members']`, `['friends']`. Keep them stable — `events.ts` references these literals.
- **Client state** → Zustand stores in `src/stores/`. Read inside selectors (`useStore(s => s.x)`); update via store actions only.
- **Forms** → react-hook-form + zod via `@hookform/resolvers`.

## Routing & layout

`react-router` v7 (`/rooms`, `/rooms/:id`, direct chats, profile, friends, sessions, auth pages). The chat layout is in [src/features/layout/](src/features/layout/); the message list uses `react-virtuoso` for virtualization.

## Tooling

- `npm run dev` — Vite dev server, proxies to BE at `https://localhost:7071`.
- `npm run build` — `tsc -b && vite build`.
- `npm test` / `npm run test:watch` — vitest.
- MSW is available (`msw` dev dep) for API mocks in tests.

## Conventions

- Use the `@/` alias, not relative `../../..` paths.
- Tailwind + DaisyUI utility classes; avoid bespoke CSS.
- Always thread `queryClient` and stores through hooks/components rather than importing them inline in deep utilities — keeps testing tractable.
- Don't create a second `HubConnection` — always go through `getHubConnection()`.
- New backend events: extend the payload interfaces at the top of [src/realtime/events.ts](src/realtime/events.ts) and add the matching `hub.on(...)` + cleanup `hub.off(...)` pair.
- Keep DTO types in [src/types/](src/types/) aligned with the backend; mismatches surface as silent cache shape bugs.
