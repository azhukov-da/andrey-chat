# FE UI Implementation Plan — Section 4 (UI Requirements)

Source: `2026_04_18_AI_herders_jam_-_requirements_v3 1.docx`, §4 + Appendix A wireframes.

## Pages identified

- **Pre-login (public):** Sign In, Register, Forgot Password, Reset Password
- **Authenticated General Layout:** Top menu + Rooms/Contacts sidebar (right, accordion) + Main chat + Members/Context panel (right)
- **Top menu tabs:** Public Rooms · Private Rooms · Contacts · Sessions · Profile ▼ · Sign out
- **Modals:** Create Room, Manage Room (Members / Admins / Banned users / Invitations / Settings), Friend Request, Delete Account

## Status matrix

| # | Page / Component | Status | File |
|---|---|---|---|
| 1 | Sign In | Done | `src/features/auth/SignIn.tsx` |
| 2 | Register | Done | `src/features/auth/Register.tsx` |
| 3 | Forgot Password | Done | `src/features/auth/ForgotPassword.tsx` |
| 4 | Reset Password | Done | `src/features/auth/ResetPassword.tsx` |
| 5 | Auth guard → force `/login` | Done | `src/features/layout/AppShell.tsx:13` |
| 6 | Top menu | Partial — missing **Private Rooms** and **Sessions** links | `src/features/layout/TopNav.tsx` |
| 7 | Right sidebar: rooms + DMs (accordion, unread badges) | Done | `src/features/layout/RightSidebar.tsx` |
| 8 | Members / Context panel (room info, admins, members w/ presence, Invite, Manage room) | Missing | — |
| 9 | Public Rooms catalog (search, join, create) | Done | `src/features/rooms/PublicCatalog.tsx` |
| 10 | Private Rooms view (own/joined + pending invitations) | Missing | — |
| 11 | Chat Window header + messages + composer + unread | Partial (see 12–14) | `src/features/chat/ChatWindow.tsx` |
| 12 | Messages: infinite scroll + autoscroll; edited indicator + reply quote | Partial — verify edited/reply UI in `MessageItem` | `src/features/chat/MessageList.tsx` |
| 13 | Composer: Attach button + real upload | Partial — paste is placeholder; no explicit Attach button | `src/features/chat/MessageComposer.tsx:68` |
| 14 | Composer: Emoji picker | Missing | — |
| 15 | Contacts (friends, requests, block, DM) | Done | `src/features/friends/FriendList.tsx` |
| 16 | Sessions page (list, log out selected) | Missing | — |
| 17 | Profile: account info, display name, delete account | Partial — missing **Change password** | `src/features/profile/ProfilePage.tsx` |
| 18 | Create Room dialog | Done | `src/features/rooms/CreateRoomDialog.tsx` |
| 19 | Manage Room dialog (Members / Admins / Banned / Invitations / Settings) | Missing | — |

## Phased plan

### Phase 1 — Complete General Layout shell
1. New `src/features/layout/MembersPanel.tsx` (3rd column on `/rooms/:id`): room info (name, visibility, owner), admins list, members list with `PresenceDot`, `[Invite user]` and `[Manage room]` buttons.
2. Update `AppShell` to render `<main> + RightSidebar + MembersPanel` as a 3-column layout per wireframe.
3. Extend `TopNav` with `Private Rooms` and `Sessions` nav links.

### Phase 2 — Missing routes/pages
4. `src/features/rooms/PrivateRooms.tsx` + route `/private` — list private rooms + pending invitations (accept/decline).
5. `src/features/profile/SessionsPage.tsx` + route `/sessions` — consume `/me/sessions`; UA/IP/last-seen; per-row `Log out` + `Log out all others`.
6. Add **Change password** card to `ProfilePage` → `POST /auth/changePassword`.

### Phase 3 — Manage Room dialog (Admin UI, §4.5)
7. `src/features/rooms/ManageRoomDialog.tsx` with tabbed `<dialog>`:
   - MembersTab — search, row actions: Make/Remove admin, Ban, Remove from room
   - AdminsTab — current admins, Remove admin (except owner)
   - BannedUsersTab — username, banned by, date/time, Unban
   - InvitationsTab — invite by username (private rooms)
   - SettingsTab — name, description, visibility, Save, Delete room (owner only)
8. Wire `MembersPanel` → `[Manage room]` to open the dialog; gate tabs by role.

### Phase 4 — Composer enhancements (§4.3)
9. `AttachButton` — opens file picker; POST `/attachments`; show upload tile + progress; replace paste placeholder with real upload.
10. Emoji picker behind `[😊]`; insert at caret.

### Phase 5 — Chat polish (§4.2, §2.5.3, §2.5.4)
11. `MessageItem`: gray "edited" indicator when `editedAt` set; render replied-to quote; inline edit/delete (author/admin).
12. Disable forced autoscroll when user has scrolled up (Virtuoso `atBottomStateChange` → toggle `followOutput`).

## Auth gate note

General Layout is already gated by `AppShell.tsx:13-17` — `GET /me` failure redirects to `/login`. Only gap: preserve `returnTo` in the redirect query so `SignIn` (line 12) can return the user to where they were.
