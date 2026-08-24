# Scenario claim conventions

Every automated test declares which specified scenarios it verifies. One place to copy from, so the
14 capabilities still to be tested are test-authoring work and not decision-making work.

The requirements behind all of this are in
the `automated-testing` capability spec — the delta at
[openspec/changes/add-test-harness/specs/automated-testing/spec.md](../openspec/changes/add-test-harness/specs/automated-testing/spec.md)
until that change is archived into `openspec/specs/`; the
runbook — prerequisites, how to run each layer, where reports land — is in the
[root CLAUDE.md](../CLAUDE.md#automated-tests).

## Identifiers

```
<capability>/<requirement-slug>/<scenario-slug>
```

Derived mechanically from `openspec/specs/<capability>/spec.md`: take the text after
`### Requirement:` and `#### Scenario:`, lowercase it, collapse every run of non-alphanumeric
characters to a single `-`, trim dashes off the ends.

```markdown
### Requirement: Revoking sessions
#### Scenario: Revoking a foreign session
```

becomes

```
user-sessions/revoking-sessions/revoking-a-foreign-session
```

Nothing maintains this mapping — `tools/spec-coverage` re-derives it from the spec files on every
run. Renaming a scenario therefore breaks the claims pointing at the old name, deliberately: the
next run fails naming the stale identifier and the test that carries it, rather than quietly
reporting the scenario as untested.

To see the full list of identifiers, including which are still uncovered, run
`node tools/spec-coverage` and read `docs/test-coverage/scenarios.md`.

## How each layer declares a claim

All three put the marker `@spec:<identifier>` in the test's **display name**. That is the one field
every runner is guaranteed to carry into its machine-readable output; each runner's metadata channel
(xUnit traits, vitest task meta, Playwright tags) serialises differently and some drop through the
TRX writer entirely. It also means the claim is visible in ordinary runner output with no tooling.

A test covering more than one scenario carries more than one marker.

### Backend integration (xUnit)

The claim is a `const string`, because an attribute argument has to be a compile-time constant.
Each capability gets a constants class next to
[`UserSessionsSpec`](../BE/Tests.Integration/Harness/IntegrationTest.cs):

```csharp
public static class RoomModerationSpec
{
    private const string Banning = "@spec:room-moderation/banning-a-member/";

    public const string BannedMemberCannotRejoin = Banning + "banned-member-cannot-rejoin ";
}
```

Then, in a test deriving from `IntegrationTest`:

```csharp
[Fact(DisplayName = RoomModerationSpec.BannedMemberCannotRejoin
                    + "a banned member's join request is refused")]
public async Task Banned_member_cannot_rejoin() { ... }
```

Note the trailing space inside each constant, so the marker and the description do not run together.
Concatenating two constants is itself a constant, so several markers compose the same way.

### Frontend unit and component (vitest)

Use [`specTest`](../FE/src/test/specTest.ts), which prefixes the marker onto the title:

```ts
import { specTest } from '@/test/specTest'

specTest(
  'user-sessions/session-registration-per-browser/restored-login-without-a-session',
  'a restored login with no stored session registers a new one',
  async () => { ... }
)
```

Pass an array of identifiers for a test covering several scenarios. `specTest.only` and
`specTest.skip` exist and behave like vitest's.

### End-to-end (Playwright)

Use the identically shaped [`specTest`](../FE/e2e/spec.ts) from `e2e/spec.ts`:

```ts
import { specTest } from './spec'

specTest(
  'user-sessions/revoking-sessions/revoking-the-current-session',
  'revoking the current session returns the browser to sign-in',
  async ({ signedIn }) => { ... }
)
```

One wart: because the helper wraps `test()`, Playwright reports the test's source location as
`e2e/spec.ts` rather than the spec file. The title in the report is still correct, and so is the
traceability. Call `test()` directly if you need the location and add the marker by hand.

## Choosing a layer

Put a scenario at the layer that can observe its outcome most directly. A scenario observable at more
than one layer does not have to appear at every layer.

| The behaviour is… | Layer |
|---|---|
| Enforced by the backend no matter which client calls it | Backend integration |
| What the UI shows, enables, or blocks before contacting the backend | Frontend unit / component |
| One user's action becoming visible in another's open session, or anything spanning browser → nginx → backend → database | End-to-end |
| One connected client observing an event another caused | Backend integration (two `TestHubClient`s) — or end-to-end when the browser's own reaction is the point |

`user-sessions` is the worked example: server-side ownership rules at the backend layer, the
session-header and registration behaviour at the frontend layer, and revoke-kicks-the-browser at the
end-to-end layer.

## What counts as covered

Only a **passing** test. A failing or skipped test's claims are reported separately as "claimed by a
test that did not pass", which is more useful than showing the scenario as untested.

## Writing tests at each layer

- **Backend** — derive from `IntegrationTest`. `CreateUserAsync()` gives a registered, signed-in user
  with an `HttpClient` already carrying `Authorization` and `X-Session-Id`. `ConnectHubAsync(user)`
  gives an authenticated hub client; claim an event with `Expect<T>(name)` *before* triggering it,
  then await it. Never sleep — the expectation completes the moment the event lands.
- **Frontend** — `renderWithProviders(<Component />, { route })` supplies a fresh query client, a
  router, and a signed-in auth store. Override one endpoint with `server.use(...)`; the shared
  defaults in `src/test/msw/handlers.ts` cover the rest, and an endpoint with no handler fails the
  test by name. Drive server pushes with `fakeHub.emit(event, payload)` after
  `vi.mock('@/realtime/hubClient', () => import('@/test/fakeHubClient'))`.
- **End-to-end** — take the `signedIn` fixture for one user or `twoUsers` for two independent browser
  contexts. Each fixture registers a fresh account with a generated identifier. The helpers in
  `e2e/fixtures.ts` cover what these tests keep needing:

  | Helper | What it does |
  |---|---|
  | `signIn(page, account, { keepSignedIn })` | Signs in through the real screen; the option ticks "Keep me signed in", which is what decides whether the refresh token goes to `localStorage` or `sessionStorage` |
  | `reopenBrowser(user, browser)` | Closes the context and opens a new one from its `storageState` — a real browser restart, since `storageState` carries `localStorage` but not `sessionStorage` |
  | `createRoom(page, name)` / `joinRoom(page, name)` | Drive the room UI and return the room id. Deliberately not seeded over HTTP: seeding through the API would skip the proxy path these tests exist to exercise |
  | `recordRequests(page)` | Collects every URL the page contacts, HTTP and WebSocket, for the transport-boundary requirement |

  Two things about the running stack are worth knowing before writing a test against it. The hub
  falls back to long polling — the WebSocket upgrade does not complete through the deployed nginx —
  so a reconnect opens no new socket and is observed through `/hubs/chat/negotiate` instead. And
  `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])` gives up permanently once its five delays
  are spent, so an induced outage has to be shorter than about eighteen seconds for the client to
  come back at all.
