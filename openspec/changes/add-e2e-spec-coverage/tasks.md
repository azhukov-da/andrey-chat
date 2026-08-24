## 1. Fixtures

- [x] 1.1 Give `signIn` in [FE/e2e/fixtures.ts](../../../FE/e2e/fixtures.ts) an options argument carrying `keepSignedIn`, ticking the "Keep me signed in" checkbox when set; verify the existing `user-sessions.spec.ts` calls still pass unchanged (D8)
- [x] 1.2 Add `reopenBrowser(user, browser)`: capture `context.storageState()`, close the context, open a fresh one seeded from that state, and return the new context and page — documenting why this models a closed browser and that `sessionStorage` is deliberately not carried over (D2)
- [x] 1.3 Add `createRoom(page, name)` and `joinRoom(page, name)`, driving the real UI and returning the room id from the resulting URL (D8)
- [x] 1.4 Add `recordRequests(page)`: attach `page.on('request')` and `page.on('websocket')` listeners and return a handle exposing the collected URLs (D8)
- [x] 1.5 Run the existing suite (`cd FE && npm run e2e`) and confirm both `user-sessions` tests still pass, so the fixture changes are established as non-breaking before anything is built on them

## 2. Persistent login

- [x] 2.1 Create `FE/e2e/authentication.spec.ts` with the doc comment explaining why these two scenarios sit at this layer
- [x] 2.2 Write `@spec:authentication/persistent-login/keep-me-signed-in`: sign in with the checkbox set, reopen the browser, assert an authenticated screen renders without re-entering credentials (D2)
- [x] 2.3 Write `@spec:authentication/persistent-login/not-persisted`: sign in with the checkbox clear, reopen the browser, assert the sign-in screen is shown (D2)
- [x] 2.4 Run both and record the outcome; if either diverges from the specification, apply D7 rather than adjusting the assertion

## 3. Room deletion while viewing

- [x] 3.1 Create `FE/e2e/room-deletion.spec.ts` with the doc comment explaining why the two claimed scenarios are one flow (D3)
- [x] 3.2 Write the two-user flow claiming both `@spec:chat-rooms/room-deletion/member-is-viewing-the-deleted-room` and `@spec:realtime-delivery/pushed-event-kinds/room-deletion-while-viewing`: owner creates a room, member joins and opens it, owner deletes and confirms
- [x] 3.3 Assert the member's page leaves the room URL without a reload and the room is gone from their list (D3)
- [x] 3.4 Run it and record the outcome; apply D7 on divergence

## 4. Sign out from the user menu

- [x] 4.1 Create `FE/e2e/navigation.spec.ts`
- [x] 4.2 Write `@spec:chat-ui-shell/top-navigation/signing-out-from-the-menu`: open the user-menu dropdown, choose Sign Out, assert the sign-in screen is shown and local credentials are cleared
- [x] 4.3 Assert the second half of the requirement — that the session is *revoked*, checked from a second browser signed into the same account whose sessions list no longer shows it (D7)
- [x] 4.4 Run it; if revocation does not happen, mark the test `specTest.skip` with the reason and add the row to [docs/spec-gaps.md](../../../docs/spec-gaps.md) naming `TopNav.tsx` and the `useAuth` logout path — do not change the source (D7)

## 5. Transport boundary

- [x] 5.1 Create `FE/e2e/platform.spec.ts`
- [x] 5.2 Write `@spec:platform-constraints/client-server-transport-boundary/proxied-call`: record requests across a representative signed-in session (sign in, rooms list, open a room, post a message, upload a small attachment)
- [x] 5.3 Assert no recorded HTTP or websocket URL has an origin other than the `baseURL`, comparing the websocket on host and port rather than scheme (D4)
- [x] 5.4 Assert the recorded set positively includes the REST, identity, hub, and attachment categories the requirement names, so the test cannot pass vacuously; if the upload path is unreachable through the UI, drop that one category and record why (D4)

## 6. Deployment topology

- [x] 6.1 Write `@spec:platform-constraints/deployment-topology/bringing-the-stack-up` in `FE/e2e/platform.spec.ts`
- [x] 6.2 Assert the frontend is reachable in a browser, and that `docker compose -f docker-compose.yml config --format json` shows neither `be` nor `db` publishing host ports — with the comment explaining why `docker-compose.tests.yml` is deliberately excluded (D5)
- [x] 6.3 Assert a direct request to `http://localhost:8080` fails to connect while the same operation succeeds through `http://localhost:3000` (D5)
- [x] 6.4 Make a missing `docker` CLI fail with a message naming that prerequisite, matching how `global-setup.ts` reports a stopped stack (D5)

## 7. Presence after reconnect

- [x] 7.1 Create `FE/e2e/realtime.spec.ts` with its own per-test timeout, since the escalating reconnect delays make it the slowest test in the suite (D6)
- [x] 7.2 Write `@spec:realtime-delivery/automatic-reconnection/presence-after-reconnect`: two users with B visible in A's presence view, take A offline with `context.setOffline(true)`, wait for the client to notice the drop
- [x] 7.3 Close B's context while A is offline, bring A back online, and assert A shows B as offline once reconnected — the assertion that distinguishes a re-requested presence from a stale one (D6)
- [x] 7.4 Run it and record the outcome; apply D7 on divergence

## 8. Verify and document

- [x] 8.1 Run the full end-to-end suite and confirm every new test either passes or is a `specTest.skip` with a `docs/spec-gaps.md` row
- [x] 8.2 Run `test.bat` and confirm `docs/test-coverage/scenarios.md` reports all nine End-to-end scenarios as claimed, with no unknown-identifier failure from `tools/spec-coverage`
- [x] 8.3 Update the end-to-end section of [docs/testing-conventions.md](../../../docs/testing-conventions.md) with the new fixtures
- [x] 8.4 Confirm `git status` shows no modification under `FE/src/`, `BE/`, `docker-compose.yml`, or `FE/nginx.conf` — the constraint this change is built around
- [x] 8.5 Report which scenarios pass and which are documented gaps, plainly and separately
