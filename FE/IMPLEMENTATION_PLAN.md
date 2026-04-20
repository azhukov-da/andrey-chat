# Front-End Implementation Plan — Classic Web Chat

## Context

Requirements from `2026_04_18_AI_herders_jam_-_requirements_v3 1.docx` describe a classic web chat: registration/login, public + private rooms, 1-to-1 personal messages, contacts/friends with user-to-user ban, file/image attachments, moderation, presence (online/AFK/offline), unread indicators, and persistent history with infinite scroll. Target scale is 300 concurrent users, rooms up to 1000 members, 10k+ messages per room.

The `FE/` folder is empty — this plan creates the front-end from scratch. The back-end it talks to is described in `BE/IMPLEMENTATION_PLAN.md` (Identity API bearer tokens, REST under `/api` and `/auth`, SignalR hub at `/hubs/chat`, files at `/files`).

## Locked decisions

| Decision | Choice |
|---|---|
| Framework | **Vite + React 19 + TypeScript `strict`** (plus `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`) |
| Styling | **Tailwind CSS + DaisyUI** (themes: `light`, `dark`) |
| Server state | **TanStack Query v5** — REST cache, infinite scroll |
| Client state | **Zustand** — auth, presence map, unread map, UI state, SignalR connection state |
| Real-time | **`@microsoft/signalr`** — single hub connection, bearer token via `accessTokenFactory` |
| Routing | **React Router v7** (data router, nested routes) |
| Forms | **React Hook Form + Zod** (schema-first validation, inferred types) |
| Testing | **Vitest + React Testing Library**; Playwright smoke test (optional) |

## Folder layout

```
FE/
├── index.html
├── package.json
├── vite.config.ts
├── tailwind.config.ts
├── postcss.config.cjs
├── tsconfig.json                (strict, noUncheckedIndexedAccess, exactOptionalPropertyTypes)
├── tsconfig.node.json
├── Dockerfile                   (node:22-alpine build → nginx:alpine serve)
├── nginx.conf                   (SPA fallback + /api /auth /hubs /files proxy)
├── .env.development             (VITE_API_BASE for `npm run dev`)
├── public/
└── src/
    ├── main.tsx
    ├── App.tsx
    ├── routes.tsx
    ├── api/
    │   ├── client.ts            # fetch wrapper, attaches bearer, refresh on 401
    │   ├── auth.ts              # /auth/register, /auth/login, /auth/refresh, /auth/forgotPassword, /auth/resetPassword
    │   ├── me.ts                # /me, /sessions
    │   ├── rooms.ts             # /rooms, /rooms/:id/members, /rooms/:id/bans, /rooms/:id/invitations
    │   ├── messages.ts          # /rooms/:id/messages, /messages/:id
    │   ├── attachments.ts       # /attachments, /files/:id
    │   ├── friends.ts           # /friends, /friends/requests, /blocks
    │   └── directChats.ts       # /direct-chats
    ├── realtime/
    │   ├── hubClient.ts         # SignalR connection factory + lifecycle
    │   ├── events.ts            # typed event handlers → query cache / zustand updates
    │   └── presencePing.ts      # 20s Ping(active), BroadcastChannel cross-tab dedup, visibility + input listeners
    ├── stores/
    │   ├── authStore.ts         # tokens, me, keepSignedIn
    │   ├── presenceStore.ts     # Map<userId, 'online' | 'afk' | 'offline'>
    │   ├── unreadStore.ts       # Map<roomId, number>
    │   └── uiStore.ts           # sidebar collapsed, active room, composer drafts
    ├── features/
    │   ├── auth/                # SignIn, Register, ForgotPassword, ResetPassword pages
    │   ├── rooms/               # RoomList, PublicCatalog, CreateRoomDialog, ManageRoomDialog (tabs)
    │   ├── chat/                # ChatWindow, MessageList (virtualized), MessageItem, MessageComposer, ReplyBar, AttachmentTile
    │   ├── friends/             # FriendList, FriendRequestDialog, BlockList
    │   ├── profile/             # ProfilePage, SessionsPage, DeleteAccountDialog
    │   └── layout/              # AppShell, TopNav, RightSidebar, MembersPanel
    ├── hooks/
    │   ├── useAuth.ts           # login/logout/register + bootstrap
    │   ├── useRoom.ts           # single room metadata
    │   ├── useMessages.ts       # useInfiniteQuery for history, merges SignalR pushes
    │   ├── usePublicRooms.ts    # useInfiniteQuery with debounced search
    │   ├── usePresence.ts       # subscribe to presenceStore
    │   ├── useUnread.ts         # derived from unreadStore + markRead
    │   └── useSignalR.ts        # starts hub after auth, handles reconnect
    ├── lib/
    │   ├── formatTime.ts
    │   ├── afkDetector.ts       # tab-local activity tracker
    │   ├── broadcastChannel.ts
    │   └── classNames.ts
    └── types/                   # zod schemas + inferred TS types mirroring BE DTOs
```

## Routing

- `/` → redirect to `/rooms` (authed) or `/login` (not).
- `/login`, `/register`, `/forgot`, `/reset?token=&email=`
- `/rooms` — layout route with right sidebar; index = public catalog.
- `/rooms/:id` — chat view.
- `/contacts` — friend list; `/contacts/:userId` — direct chat (resolves via `POST /direct-chats` to a Room id and internally navigates to `/rooms/{resolvedId}`).
- `/sessions`, `/profile`.
- `AuthGuard` wraps the chat layout: checks `me` query; unauth → redirect to `/login` preserving `returnTo` in query.

## Auth

- On successful login: store `accessToken` in memory + `sessionStorage`; if "Keep me signed in" is checked, store `refreshToken` in `localStorage`; otherwise in `sessionStorage`.
- On boot: hydrate `authStore` from storage; call `GET /me` to validate; if it fails, try `POST /auth/refresh` once; on second failure, clear store + go to `/login`.
- `api/client.ts` fetch wrapper: injects `Authorization: Bearer {accessToken}`. On 401, queues concurrent requests, attempts a single refresh, then replays. On refresh failure: dispatch `auth/logout` event.
- Password reset flow: `/forgot` posts email; BE (dev) logs link; user clicks `/reset?token=...&email=...` → posts new password to `/auth/resetPassword`.

## Real-time

Single SignalR connection (created by `useSignalR` after `me` loads):

- `accessTokenFactory: () => authStore.getState().accessToken`
- `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])`
- Start URL: `/hubs/chat` (same origin via nginx proxy).

Event handlers (`realtime/events.ts`) dispatch to:

| Event | Handler |
|---|---|
| `MessageReceived` | `queryClient.setQueryData(['messages', roomId], …)` prepend to first page; bump `unreadStore` if room not active |
| `MessageEdited` / `MessageDeleted` | patch cached message |
| `PresenceChanged` | `presenceStore.update(userId, state)` |
| `RoomMembershipChanged` | `queryClient.invalidateQueries(['rooms', 'mine'])` and `['rooms', roomId, 'members']` |
| `RoomDeleted` | invalidate rooms, if active room → navigate to `/rooms` |
| `FriendRequestReceived` | invalidate friends, toast |
| `UnreadUpdated` | sync authoritative counter from server |

Outgoing hub calls: `SendMessage`, `EditMessage`, `DeleteMessage`, `StartTyping`, `StopTyping`, `Ping(active)`, `MarkRead`.

Presence ping (`realtime/presencePing.ts`):
- `setInterval(20_000)` → `hub.invoke('Ping', active)` where `active = document.visibilityState === 'visible' && lastUserInputWithin60s`.
- Input listeners: `mousemove`, `keydown`, `touchstart`, `wheel` (throttled).
- `BroadcastChannel('presence-leader')` elects one tab to ping; others piggyback.
- On `visibilitychange` or `online` event: force-ping and reconnect hub if down.

## Data fetching patterns

- `useMessages(roomId)` = `useInfiniteQuery({ queryKey: ['messages', roomId], queryFn: ({ pageParam }) => api.getMessages(roomId, { before: pageParam, limit: 50 }), getNextPageParam: last => last.nextCursor })`. Renders newest first; SignalR-pushed new messages `setQueryData` inserts into first page.
- Optimistic send: mutation adds `{ id: `temp-${nonce}`, status: 'sending', …}`; on hub echo with matching `nonce`, replace; on error, mark `failed` and allow retry.
- `usePublicRooms(search)` = `useInfiniteQuery` with 250 ms-debounced search param.
- `useUnread()` returns `{ total, perRoom[] }` derived from `unreadStore`; `markRead(roomId)` calls `POST /rooms/:id/read` and zeros local count.

## UI mapping to wireframes (Appendix A)

- **Top menu** (Public/Private/Contacts/Sessions/Profile/Sign out) → `navbar` + `menu menu-horizontal`.
- **Right sidebar** with rooms + contacts → DaisyUI `drawer` becoming a `collapse collapse-arrow` accordion once a room is open (wireframe 4.1.1).
- **Public Rooms catalog** — `card` per room with name, description, `badge` showing member count. Search input debounced.
- **Room chat view** — header with name + description; `MessageList` virtualized via `react-virtuoso`; each message is a `chat chat-start`/`chat chat-end` bubble with DaisyUI `chat-bubble`.
- **Replied message preview** — left-border quote block on top of the replied message (`border-l-4 border-primary pl-2 text-sm opacity-70`).
- **"edited" indicator** — `<span class="text-xs opacity-50">edited</span>` after timestamp.
- **Composer** — `textarea textarea-bordered` with `maxLength={3072}`; icon buttons (`btn btn-ghost btn-sm btn-circle`) for emoji (`emoji-picker-react`), attach (`<input type="file">`), reply chip (X to clear).
- **Attachment tile** — `card card-compact bg-base-200` with filename, size, optional comment, download button.
- **Paste upload** — global `paste` listener on the composer container captures `clipboardData.files`.
- **Manage Room dialog** — `dialog.modal` with `tabs tabs-boxed`: Members, Admins, Banned, Invitations, Settings. Tables use DaisyUI `table`.
- **Presence dots** — `badge badge-xs` in green/amber/gray; map `online`/`afk`/`offline` to classes `badge-success`/`badge-warning`/`badge-ghost`. Matches wireframe glyphs (● ◐ ○).
- **Unread indicator** — DaisyUI `indicator indicator-item badge badge-primary badge-sm` on room/contact rows.
- **Auth pages** — centred `card` with `form-control` fields; `Register` enforces password confirmation client-side and mirrors BE policy (min 6 chars, 1 digit, 1 uppercase).
- **Sessions page** — table of browser/IP/last-active with per-row "Log out" button.

## Forms + validation

- Shared `<Field name>` wrapper built on DaisyUI `form-control` + RHF's `useFormContext`. Displays validation errors in `label-text-alt text-error`.
- Zod schemas for every form in `types/`. Registration Zod mirrors BE policy; message composer length-validates against BE's 3 KB cap (UTF-8 byte count, not char count — use `TextEncoder.encode(text).length`).
- File inputs enforce the 20 MB / 3 MB caps client-side before upload (fast fail); BE re-validates.

## Build + Docker

- `Dockerfile`:
  - Stage 1: `node:22-alpine` — `npm ci`, `npm run build`.
  - Stage 2: `nginx:alpine` — copy `dist` + `nginx.conf`.
- `nginx.conf`:
  - `location /api/ { proxy_pass http://api:8080/api/; }`
  - `location /auth/ { proxy_pass http://api:8080/auth/; }`
  - `location /files/ { proxy_pass http://api:8080/files/; }`
  - `location /hubs/ { proxy_pass http://api:8080/hubs/; proxy_http_version 1.1; proxy_set_header Upgrade $http_upgrade; proxy_set_header Connection "upgrade"; proxy_read_timeout 1h; }`
  - Default: `try_files $uri /index.html`.
- Dev: Vite dev server on `5173` with `server.proxy` for `/api`, `/auth`, `/files`, `/hubs` → `http://localhost:8080` (BE local run).

## Testing

- **Vitest + RTL** unit/component:
  - Auth forms (validation, submit, error display).
  - Message list ordering + optimistic send reconciliation.
  - Unread counter logic.
  - AFK detector state machine.
- **Playwright** (optional single smoke): register two users in separate contexts, exchange a message, verify presence dot flips.
- **MSW** for mocking REST + a thin `hub-mock` for SignalR in component tests.

## Critical files to create

All paths under `FE/`. None exist today.

Starter package.json dependencies:
- `react`, `react-dom`, `react-router`, `@tanstack/react-query`, `@tanstack/react-query-devtools`, `zustand`, `@microsoft/signalr`, `react-hook-form`, `zod`, `@hookform/resolvers`, `emoji-picker-react`, `react-virtuoso`, `clsx`.
- Dev: `vite`, `@vitejs/plugin-react`, `typescript`, `tailwindcss`, `daisyui`, `postcss`, `autoprefixer`, `vitest`, `@testing-library/react`, `@testing-library/jest-dom`, `msw`, `@types/react`, `@types/react-dom`.

## Verification

1. `npm run build` — succeeds with strict TS and no warnings.
2. `npm run dev` against a running BE:
   - Register, log in, `GET /me` succeeds; refresh page; stay signed in.
   - Create public room; open from another browser; receive the new room in catalog.
   - Send text, edit, delete — all reflected in real time on second client.
   - Send image via paste → preview + download works.
   - Close all tabs of user B → user A sees dot go AFK after 60 s, then offline.
   - Open 10k-message room → scrolling to top triggers infinite load with no visible lag.
   - Ban user B from room → user B's chat view disappears and `/files/:id` downloads 403 (rendered as "No access").
3. `npm test` — green.
4. `docker compose up --build` from repo root → hit `http://localhost/` → full flow works through nginx (WebSocket upgrade must succeed; confirm DevTools shows a `101 Switching Protocols` on `/hubs/chat`).

## Out of scope (v1)

- XMPP/Jabber UI (admin dashboard, federation stats).
- Push notifications beyond in-app indicators.
- Mobile-native clients; layout is responsive but not PWA-installable.
- i18n (English only, though all strings centralised for easy extraction later).
