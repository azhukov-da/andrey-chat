## Context

See [proposal.md](proposal.md) — Why. The design-relevant state of the layer today:

- [FE/playwright.config.ts](../../../FE/playwright.config.ts) drives `http://localhost:3000` — the frontend's own nginx — with no `webServer` block, 2 workers, a 60s per-test timeout and `retries: 0`. `e2e/global-setup.ts` fails the run with an actionable message when the stack is not serving.
- [FE/e2e/fixtures.ts](../../../FE/e2e/fixtures.ts) offers `createUser`, `signedIn`, `twoUsers`, `registerAccount`, `signIn`, and `storedSessionId`. Users are generated per test and nothing is cleaned up; this layer runs against the developer's own database by design.
- `specTest(scenarioIds, title, fn)` in [FE/e2e/spec.ts](../../../FE/e2e/spec.ts) prefixes `@spec:` markers onto the Playwright test title, where `tools/spec-coverage` reads them out of the JSON report.
- Sign-in and registration are only reachable by client-side routing from `/`, because nginx proxies `/login` and `/register` to Identity. `openSignInScreen` already encodes that workaround.
- Token storage ([FE/src/stores/authStore.ts](../../../FE/src/stores/authStore.ts)): the access token always lives in `sessionStorage`; the refresh token goes to `localStorage` when "keep me signed in" was chosen and to `sessionStorage` otherwise. `keepSignedIn` is *derived* on load from the presence of a `localStorage` refresh token.

The eight scenarios to cover, from [docs/test-layer-triage.md](../../../docs/test-layer-triage.md):

| Scenario | What only the assembled system shows |
|---|---|
| `authentication/persistent-login/keep-me-signed-in` | Credentials surviving a closed browser |
| `authentication/persistent-login/not-persisted` | Credentials not surviving one |
| `chat-rooms/room-deletion/member-is-viewing-the-deleted-room` | One user's delete moving another user's browser |
| `realtime-delivery/pushed-event-kinds/room-deletion-while-viewing` | The same flow, viewed as a push-channel obligation |
| `chat-ui-shell/top-navigation/signing-out-from-the-menu` | Menu sign-out revoking the session and returning to sign-in |
| `platform-constraints/client-server-transport-boundary/proxied-call` | Which origins the browser actually contacts |
| `platform-constraints/deployment-topology/bringing-the-stack-up` | Which services the compose stack publishes |
| `realtime-delivery/automatic-reconnection/presence-after-reconnect` | Presence reconverging after a real interruption |

## Goals / Non-Goals

**Goals:**

- One passing (or explicitly skipped and documented) end-to-end test per scenario above, claiming its identifier so `tools/spec-coverage` stops reporting it as uncovered.
- Fixtures that express the new capabilities once — browser-session restart, room setup with a second member, network observation — rather than repeating them per test.
- Tests that fail for a real reason. Every assertion traces to a sentence in the specification, and a failure names which sentence.

**Non-Goals:**

- Any change to application source. This is the user's explicit constraint and it also protects the exercise: a test that can be satisfied by editing the thing it measures measures nothing.
- Re-verifying at this layer what the backend or frontend layer already owns. The triage document is the authority on which layer owns what; this change adds exactly the End-to-end rows.
- The load and capacity layer. The `platform-constraints` capacity and latency scenarios stay outside the gated run.
- Turning on the line-coverage gate. That is a separate decision, unaffected here.

## Decisions

### D1 — Files grouped by capability, not one file per scenario

New files: `FE/e2e/authentication.spec.ts`, `FE/e2e/room-deletion.spec.ts`, `FE/e2e/navigation.spec.ts`, `FE/e2e/platform.spec.ts`, `FE/e2e/realtime.spec.ts`. This matches the existing `user-sessions.spec.ts` precedent — file named for the capability, with a doc comment explaining why the scenarios in it belong at this layer.

*Alternative considered:* one `e2e.spec.ts`. Rejected: Playwright parallelises at file granularity, and a single file would serialise the two slow multi-user tests behind everything else.

### D2 — "Closing and reopening the browser" is a new BrowserContext seeded with localStorage only

Playwright's `storageState` captures cookies plus origin `localStorage`; it does not capture `sessionStorage`. That asymmetry is exactly the distinction the specification draws, so the fixture is a faithful model of closing a browser rather than an approximation of one:

- **Keep me signed in**: sign in with the checkbox set, capture `context.storageState()`, close the context, open a new one from that state, navigate to `/`. Expect an authenticated screen without re-entering credentials.
- **Not persisted**: sign in with the checkbox clear, capture state, reopen the same way. Expect the sign-in screen.

The reopened context genuinely is a different browser session with the same persistent storage, which is what "the browser session ends" means. Note that in the keep-me-signed-in case the access token is also gone from the reopened context, since it lived in `sessionStorage` — so the restored session depends on the refresh token being exchanged at `/refresh` before an authenticated screen renders. The test must therefore wait on the screen, not on a token value.

*Alternative considered:* clearing `sessionStorage` by script and reloading in place. Rejected: it tests the code's use of storage rather than the browser's behaviour, and it would pass even if the app kept an in-memory token that a real restart would lose.

### D3 — The room-deletion pair is one two-user flow claiming two identifiers

`chat-rooms/room-deletion/member-is-viewing-the-deleted-room` and `realtime-delivery/pushed-event-kinds/room-deletion-while-viewing` describe the same moment from two capabilities' points of view: the room owner deletes, and the viewing member is navigated away while their lists refresh. `specTest` already accepts an array of identifiers for precisely this. One test, both claims — running the identical flow twice would double the cost of the slowest test in the suite to assert the same thing.

The flow: the owner creates a public room; the second user joins it and opens it; the owner deletes and confirms; the member's page is asserted to leave the room URL and the room is asserted absent from their list. The member's browser is never reloaded — being moved without a reload is the observable behaviour.

*Alternative considered:* separate tests, with the realtime one asserting on the raw hub event. Rejected: intercepting the SignalR frame would test the transport, while the specification's THEN is about the room lists and the navigation, both visible in the DOM.

### D4 — proxied-call is asserted by recording every request the page makes

Attach a `page.on('request')` listener for the whole of a representative signed-in session — sign in, open the rooms list, open a room, post a message — then assert that no recorded URL has an origin other than the `baseURL`, and that the recorded set actually includes the categories the requirement names: the REST API (`/api/...`), identity (`/login`, `/refresh`, `/manage/...`), and the hub (`/hubs/chat`, including its websocket upgrade). Asserting only "nothing off-origin" would pass on a page that made no backend calls at all, so both halves are needed.

Attachment content (`/uploads/...`) is named by the requirement but only appears once something has been uploaded; the test uploads a small file so the category is genuinely observed rather than assumed. If the upload path proves unreachable through the UI, the assertion for that one category is dropped and the reason recorded, rather than being asserted vacuously.

The websocket is observed through `page.on('websocket')`, whose URL is checked for the same origin — a `ws://` URL against an `http://` baseURL is compared on host and port, not on scheme.

*Alternative considered:* grepping `FE/src` for `localhost:8080`. Rejected: the requirement is about what the frontend does at runtime, and a static grep cannot see a URL assembled from parts or injected by configuration.

### D5 — bringing-the-stack-up reads the compose topology from the host and probes the negative halves

Three assertions, matching the requirement's three clauses:

1. **The frontend is reachable in a browser** — implied by every other test, asserted here directly by loading `/` and getting the SPA.
2. **The backend is reachable only through the frontend's proxy** — `docker compose config --format json` shows the `be` service publishes no host ports, and a direct request to `http://localhost:8080` from the test process fails to connect, while the same operation through `http://localhost:3000/api/...` succeeds.
3. **The database is not exposed outside the internal network** — the `db` service publishes no host ports.

Both topology assertions read the base compose file specifically. `docker-compose.tests.yml` deliberately publishes `55432` so the backend integration layer can reach Postgres; that overlay is a test-time concern and not the deployed topology the requirement constrains. The test therefore invokes `docker compose -f docker-compose.yml config` with the overlay excluded, and says so in a comment, so a future reader does not "fix" the test by including it.

This test shells out to `docker`. When `docker` is not on the path it fails with a message naming that prerequisite, consistent with how `global-setup.ts` handles a stopped stack.

*Alternative considered:* parsing `docker-compose.yml` as YAML directly. Rejected: `docker compose config` is the resolved topology after profiles, overrides, and variable substitution — parsing the file by hand would assert what was written rather than what runs.

### D6 — presence-after-reconnect interrupts the network at the browser, not at the server

Two users, both signed in, with user B visible in user A's presence-bearing view. Then, in A's page:

1. `context.setOffline(true)` — the hub connection drops for real; SignalR sees a transport failure, not a graceful close.
2. Wait for the client to notice, so the reconnect being tested is a real one.
3. `context.setOffline(false)` — `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])` reconnects without user action.
4. Assert that presence for the displayed users is correct again.

The specification's THEN is "presence for the users currently displayed is requested again so stale indicators are corrected". To make that observable rather than vacuous, B's presence is changed while A is offline — B's context is closed, so B goes offline — and A is asserted to show B as offline after reconnecting. A client that reconnected but did not re-request presence would still be showing the stale online indicator, so the assertion distinguishes the two outcomes. [FE/src/features/layout/RightSidebar.tsx](../../../FE/src/features/layout/RightSidebar.tsx) registers `hub.onreconnected` and re-invokes `GetPresenceFor`, so the view under test is the one carrying the behaviour.

The escalating reconnect delays mean this test needs a generous wait — up to roughly 20 seconds for presence to converge — which is why it gets its own file and an explicit per-test timeout rather than relying on the suite default.

*Alternative considered:* restarting the `be` container. Rejected: far slower, it disrupts every other test running in parallel against the shared stack, and it exercises server startup rather than client reconnection.

### D7 — Divergences become skipped tests plus a docs/spec-gaps.md row, never a source edit

The established convention, already used by the backend layer. When a test written from the specification fails and inspection shows the implementation genuinely behaves differently:

- The test keeps its spec-accurate assertions and gains `specTest.skip`, with a comment naming the divergence and the source location.
- A row goes into [docs/spec-gaps.md](../../../docs/spec-gaps.md): expected behaviour, observed behaviour, source location.
- Coverage then reports the scenario as claimed-but-not-passing, which is the honest state — better than either a deleted test or a test rewritten to match the bug.

The `signing-out-from-the-menu` scenario is the most likely candidate: it requires sign-out to *revoke the current session*, and [FE/src/features/layout/TopNav.tsx](../../../FE/src/features/layout/TopNav.tsx) calls `logout` from `useAuth`. Whether that reaches the sessions API or only clears local storage decides whether the test passes or becomes a documented gap. The test asserts both halves — local credentials cleared *and* the session gone server-side, checked from a second browser on the same account — so the outcome distinguishes them instead of blurring them.

### D8 — Fixture additions, and where they go

All in `FE/e2e/fixtures.ts`, alongside the existing helpers and following their documentation style:

| Helper | Purpose |
|---|---|
| `signIn(page, account, options)` | Existing helper gains an options argument carrying `keepSignedIn`; current call sites keep today's behaviour |
| `reopenBrowser(user, browser)` | Captures `storageState`, closes the context, returns a fresh context and page seeded from it |
| `createRoom(page, name)` | Creates a room through the UI and returns its id from the resulting URL |
| `joinRoom(page, name)` | Joins a public room by name from the rooms list |
| `recordRequests(page)` | Attaches request and websocket listeners, returning a handle that exposes the collected URLs |

`createRoom` and `joinRoom` drive the real UI rather than the API, because a test that seeded through HTTP would not exercise the proxy path it is partly there to verify.

## Risks / Trade-offs

- **The two multi-user tests are the slowest in the suite and share one stack with the rest of the run** → Both are bounded by explicit `expect` timeouts well under the per-test limit, and `retries: 0` stays, so flakiness surfaces as a failure rather than being papered over. If either proves unstable in practice, the honest response is to record why, not to add retries.
- **`setOffline` models network loss, not every kind of disconnect** → It is the closest thing to a pulled cable available in-browser, and it is what the scenario describes. Server-side drops are a different scenario and are not claimed here.
- **`docker compose config` adds a host-tool prerequisite to one test** → Scoped to the single topology test, which fails with a message naming `docker`, matching the layer's existing convention for missing prerequisites. Every other test needs only the serving stack.
- **The end-to-end layer writes to the developer's own database** → Unchanged from today and already documented in CLAUDE.md; the new tests add rooms as well as accounts, so `docker compose down -v` clears a little more than before.
- **Some of these tests may not pass** → Expected, and rather the point. Under D7 a divergence is recorded rather than hidden, so "all eight written" and "all eight passing" are reported as the different things they are.
