## Context

See `proposal.md` — Why. Design-relevant facts about the current repository:

- `BE.slnx` contains four projects (Domain, Application, Infrastructure, Web) and no test project. `BE/Web/Program.cs` is top-level statements wrapped in `try/catch/finally`, applies EF migrations at startup, maps controllers, `MapIdentityApi<ApplicationUser>()`, and `MapHub<ChatHub>("/hubs/chat")`.
- `FE` already has `vitest`, `msw`, `@testing-library/react`, and `@testing-library/jest-dom` in `devDependencies` and an `npm test` script, but no test files, no vitest config block, and no jsdom.
- `FE/vite.config.ts` proxies to `https://localhost:7071` — a local-dev backend, not the compose stack. The compose frontend serves through nginx on `localhost:3000`, which is the only address where the assembled system is reachable.
- `docker-compose.yml` publishes a host port for `fe` only. `db` and `be` are reachable on the compose network but not from the host.
- `openspec/specs/` holds 15 capability specs with 270 `#### Scenario:` blocks in a consistent `### Requirement:` / `#### Scenario:` structure.
- `stt` is a real Python service in the compose stack with a health check and a slow (60s) start period.

## Goals / Non-Goals

**Goals:**

- A harness for each of the three gated layers that a later change can add tests to without redesigning anything.
- Scenario identifiers derived mechanically from the spec files, so the traceability report cannot drift from the specs.
- Coverage measured from the same run that verifies behavior, so the number means something.
- One command that a developer runs, plus per-layer commands for the inner loop.
- The harness proven end to end on `user-sessions` — 8 scenarios that happen to span all three layers (server-side ownership rules, client-side session-header behavior, and a revoke-kicks-the-browser flow).

**Non-Goals:**

- Meeting the 80% line-coverage gate in this change. With only `user-sessions` tested, coverage will be far below 80%. This change makes the gate *exist and be correct*; follow-up per-capability changes make it *pass*. The threshold is therefore configured but the aggregate `test.bat` reports it as informational until the final capability change flips it to enforcing.
- Test authoring for the other 14 capabilities.
- Any hosted CI configuration.
- Mutation testing, contract testing, visual regression, or accessibility auditing.

## Decisions

### D1. Backend integration substrate: in-process host against the compose PostgreSQL

`WebApplicationFactory<Program>` hosts the Web project in the test process; the database is the `db` container from `docker-compose.yml`, published to the host as `55432:5432` **by a separate `docker-compose.tests.yml` overlay**, not by the base compose file. Each xUnit *collection* gets a database named `chat_test_{guid:N}`, created over an admin connection to the `postgres` database, migrated by the app's own startup path, and dropped on fixture disposal. Within a collection, `Respawn` truncates all tables between tests rather than re-migrating.

The overlay matters: `platform-constraints`' "Bringing the stack up" scenario requires that the database not be reachable from outside the internal network. Publishing the port in the base file would make the harness violate a spec it exists to verify. With the overlay, the default `docker compose up` and `start.bat` remain spec-compliant, and the port exists only when a developer explicitly opts in with `docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db`.

*Why:* fast inner loop — no container image pull or startup on every run — while still exercising real PostgreSQL, real migrations, real Identity, and the real HTTP pipeline.

*Alternatives considered:*
- **Testcontainers** (a throwaway `postgres:17-alpine` per run) — better reproducibility and no dependency on compose being up, at the cost of 10–20s startup per run. Rejected for the inner-loop cost.
- **EF in-memory or SQLite provider** — fastest, no Docker, but migrations never run and PostgreSQL-specific ordering, collation, and concurrency semantics are silently different. The `platform-constraints` spec explicitly requires startup migrations, and moderation depends on consistency guarantees, so this layer would pass while the real system failed. Rejected outright.

*Consequences of D1:* the compose stack must be up before backend integration tests run; test databases live on the same server as the development `chat` database. The isolation requirements in the spec exist to bound this — see R1 and R2 under Risks.

### D2. `public partial class Program { }` appended to `BE/Web/Program.cs`

`WebApplicationFactory<TEntryPoint>` needs an accessible entry-point type. Top-level statements generate an `internal Program`, so either this marker or an `InternalsVisibleTo` is required. The marker is the smaller, more conventional change and adds no runtime behavior. It must sit after the top-level statements, outside the `try/catch`.

### D3. Real-time testing without ports

SignalR clients in the integration layer are built with `HubConnectionBuilder().WithUrl(new Uri(factory.Server.BaseAddress, "hubs/chat"), o => { o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler(); o.AccessTokenProvider = () => Task.FromResult(token); })`. This keeps multi-client tests (A sends, B receives) fully in-process — no listening socket, no port allocation, no polling sleeps. Event assertions use a `TaskCompletionSource` per expected event with a bounded timeout, never `Task.Delay`.

*Alternative:* run a real Kestrel host on a dynamic port. Rejected — more moving parts and reintroduces port and timing flakiness for no fidelity gain, since `TestServer` runs the same hub pipeline.

### D4. Scenario identifiers

Identifier format: `<capability>/<requirement-slug>/<scenario-slug>`, where slugs are the heading text lowercased, non-alphanumerics collapsed to `-`, trimmed. Example: `user-sessions/revoking-sessions/revoking-a-foreign-session`.

Derived by parsing `openspec/specs/**/spec.md`, not maintained by hand. Renaming a scenario in a spec therefore breaks the claim that references it — which is the intended behavior, surfacing as the "unknown scenario" failure in the spec rather than silently under-reporting.

*Alternative:* hand-assigned stable IDs written into the spec files (e.g. `<!-- id: US-07 -->`). More robust against renames, but pollutes the specs and requires discipline to maintain. Rejected; renames are rare and loud failure is better than silent drift.

### D5. How each layer declares a scenario claim

One mechanism for all three layers: **the scenario identifier lives in the test's display name**, as one or more `@spec:<identifier>` markers.

| Layer | Written as | Extracted from |
|---|---|---|
| BE integration | `[Fact(DisplayName = "@spec:user-sessions/... Lists own sessions newest first")]` | `testName` + `outcome` attributes on `<UnitTestResult>` in the `.trx` from `dotnet test --logger trx` |
| FE unit | `specTest('user-sessions/...', 'lists own sessions', fn)` — helper prefixes the marker onto the title | `assertionResults[].fullName` + `.status` in vitest `--reporter=json` |
| E2E | `specTest('user-sessions/...', 'revoking another session', fn)` — same helper shape over Playwright's `test()` | recursive walk of `suites[].specs[].title` + `tests[].results[].status` in Playwright `--reporter=json` |

*Why one mechanism:* every test runner guarantees the display name reaches its machine-readable output, whereas each runner's metadata channel (xUnit traits, vitest task meta, Playwright tags) serializes differently and some drop through the TRX writer entirely. Names are the reliable carrier. It also means a developer can see a test's scenario claim in the runner output without any tooling.

*Cost:* long test names, and a claim can only be validated by the tooling rather than by the compiler. Accepted — the "unknown scenario claim" failure catches typos on the next run.

### D6. `tools/spec-coverage` is Node, not .NET

The frontend toolchain is already Node and two of the three result formats are JSON produced by Node runners. Node also means the tool runs without the .NET SDK, so the report can be regenerated from cached results. It reads the three result files, normalizes, cross-references, and writes `docs/test-coverage/scenarios.md` plus a console summary. Non-zero exit on an unknown scenario claim; scenario percentage is reported but does not gate (only line coverage gates — see the spec's "Code coverage gate").

### D7. Coverage measurement

- **Backend**: `dotnet test --collect:"XPlat Code Coverage"` (coverlet) over the integration project, then ReportGenerator to HTML + a Cobertura summary. Excluded via `coverlet.runsettings`: migrations (`**/Migrations/**`), DTOs and generated files, test projects, `Program.cs`'s bootstrap logging.
- **Frontend**: vitest `coverage.provider: 'v8'` with `thresholds.lines: 80`, configured in `FE/vite.config.ts`. Excluded: `main.tsx`, `routes.tsx`, type-only modules, `e2e/**`, test files.
- The end-to-end and load layers are excluded from measurement entirely — they run against a separate process where instrumentation would be misleading.
- The threshold is **evaluated** by `tools/coverage-gate`, which reads both summaries and reports each measured percentage and how far short it falls. It exits non-zero only with `--enforce`. `test.bat` calls it without the flag for now, and no vitest-level `thresholds` entry is configured — a vitest threshold would fail the frontend run outright while only `user-sessions` has tests, masking real test failures behind a coverage failure. Flipping to enforcing is adding one flag.

### D8. E2E targets the compose stack at `localhost:3000`

Playwright's `baseURL` is `http://localhost:3000`, exercising the real nginx proxy configuration in `FE/nginx.conf` — which is itself part of what can break. `webServer` is *not* configured to launch anything; the suite asserts the stack is reachable in a global setup and fails with a clear "run start.bat first" message otherwise. Multi-user scenarios use two `browser.newContext()` instances rather than two browsers.

Each e2e test registers its own users with unique generated emails and cleans up nothing — e2e runs against the developer's stack and is not part of the isolation guarantee that covers the integration layer. The `stt` service is used for real in the speech-to-text flow rather than stubbed, since it is present in compose and stubbing it would test nothing.

### D9. Load suite is a separate project with its own entry point

`BE/Tests.Load` is a console project using NBomber, driving `localhost:3000` (or the backend directly when the frontend proxy is not the thing under test). It is excluded from `test.bat` and from `dotnet test` discovery, run via `dotnet run --project BE/Tests.Load -- <scenario>`. Its restart-durability scenario shells out to `docker compose restart be`. It writes measured-vs-target figures to `docs/test-coverage/load.md`.

### D10. `test.bat` orchestration

Sequence: check prerequisites (compose stack reachable, `db` on 55432, `localhost:3000` responding) → `dotnet test` (integration, with coverage) → `npm run test:coverage` (FE unit) → `npx playwright test` → `node tools/spec-coverage` → print a summary table naming each layer's result, both coverage percentages, scenario coverage, and the report paths. Per-layer commands (`dotnet test`, `npm test`, `npm run e2e`) remain usable directly for the inner loop.

## Risks / Trade-offs

- **Test databases share a server with development data (from D1)** → tests connect only to their own `chat_test_*` database; the admin connection is used solely for `CREATE DATABASE` / `DROP DATABASE` on names matching that prefix. No test code opens a connection to `chat`.
- **An aborted run leaks a `chat_test_*` database** → fixture startup drops any `chat_test_*` database older than 24 hours before creating its own. Bounded growth, no manual cleanup, and no risk of dropping a database another run is actively using.
- **Publishing PostgreSQL on a host port widens local exposure and conflicts with `platform-constraints`** → the port lives in `docker-compose.tests.yml`, never in the base compose file, so the deployed topology the spec describes is unchanged and `start.bat` still brings up a stack with no published database port. Port `55432` rather than `5432` also avoids colliding with an installed PostgreSQL.
- **Compose must be running for two of the three layers** → the prerequisite check fails fast with an actionable message rather than producing a wall of connection errors, per the spec's "Missing prerequisite" and "Database unavailable" scenarios.
- **Playwright against a shared dev stack accumulates test users and rooms** → accepted. Generated identifiers keep runs from colliding; a `docker compose down -v` resets it when clutter matters.
- **Scenario renames break claims (from D4)** → intended, and surfaced as a hard failure with the stale identifier named, so the fix is mechanical.
- **`Program.cs` gains a test-only marker (from D2)** → a comment marks why it exists so it is not "cleaned up" later.
- **The 80% gate cannot pass in this change** → it is configured but reported informationally by `test.bat` until the final per-capability change flips it to enforcing. Recorded here so a later reader does not mistake the non-enforcing state for an oversight.

## Migration Plan

Purely additive. The only production-code edits are the `Program.cs` marker and the compose port. Rollback is deleting the new projects and reverting those two edits; nothing in the running application depends on any of it.
