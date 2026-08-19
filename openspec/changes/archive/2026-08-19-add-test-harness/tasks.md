## 1. Prerequisites in production code

- [x] 1.1 Append `public partial class Program { }` to `BE/Web/Program.cs` after the top-level statements, outside the `try/catch`, with a comment explaining it exists for `WebApplicationFactory`
- [x] 1.2 Create `docker-compose.tests.yml` overlaying the `db` service with `ports: ["55432:5432"]`, leaving the base `docker-compose.yml` unchanged so `platform-constraints`' "Bringing the stack up" still holds
- [x] 1.3 Verify `docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db` then a `psql`/`Npgsql` connection to `localhost:55432` succeeds, and that a plain `docker compose up -d db` publishes no host port

## 2. Backend integration harness

- [x] 2.1 Create `BE/Tests.Integration` (xUnit v3) and add it to `BE.slnx`; add `Microsoft.AspNetCore.Mvc.Testing`, `Respawn`, `Npgsql`, `coverlet.collector`, `FluentAssertions`
- [x] 2.2 Implement `TestDatabase`: create `chat_test_{guid:N}` over an admin connection, drop on dispose, and on startup drop any `chat_test_*` database older than 24 hours
- [x] 2.3 Implement `ChatAppFactory : WebApplicationFactory<Program>` overriding `ConnectionStrings:DefaultConnection` to the per-collection test database and forcing the `Development`-independent config the tests need
- [x] 2.4 Implement a collection fixture that runs migrations via the app's own startup path and exposes a `Respawn` checkpoint reset for per-test cleanup
- [x] 2.5 Implement `AuthHelper`: register a user, sign in, return the bearer token and session id, and produce an `HttpClient` with `Authorization` and `X-Session-Id` preset
- [x] 2.6 Implement `HubClient`: build a `HubConnection` over `factory.Server.CreateHandler()` with an access-token provider, plus a `WaitFor<TEvent>(timeout)` helper backed by `TaskCompletionSource` (no `Task.Delay` anywhere)
- [x] 2.7 Implement the fail-fast database reachability check that reports the expected endpoint and how to start it
- [x] 2.8 Add `coverlet.runsettings` excluding `**/Migrations/**`, DTOs, generated files, and test projects
- [x] 2.9 Write one throwaway smoke test proving factory, database isolation, auth helper, and a two-client hub round trip all work; confirm it passes twice in a row with no cleanup

## 3. Frontend unit/component harness

- [x] 3.1 Add `jsdom` and `@vitest/coverage-v8`; add the `test` block to `FE/vite.config.ts` with `environment: 'jsdom'`, setup files, and `coverage` (v8 provider, `thresholds.lines: 80`, exclusions per design D7)
- [x] 3.2 Create `FE/src/test/setup.ts` registering `@testing-library/jest-dom` and resetting stores, query cache, and MSW handlers between tests
- [x] 3.3 Create the MSW server and shared handlers covering the endpoints `FE/src/api/*` calls, with per-test override support
- [x] 3.4 Create `renderWithProviders` wrapping a component in a fresh `QueryClientProvider` and router with seedable auth state
- [x] 3.5 Create the fake hub transport: a stand-in for `FE/src/realtime/hubClient.ts` that records invocations and lets a test emit any server→client event from `FE/src/realtime/events.ts`
- [x] 3.6 Add `test`, `test:watch`, and `test:coverage` scripts to `FE/package.json`
- [x] 3.7 Write one throwaway smoke test rendering a component, intercepting a REST call via MSW, and driving a hub event through the fake transport

## 4. End-to-end harness

- [x] 4.1 Add `@playwright/test` to `FE`; create `FE/playwright.config.ts` with `baseURL: http://localhost:3000`, no `webServer`, JSON reporter, and trace/screenshot on failure
- [x] 4.2 Create a global setup that asserts `localhost:3000` is reachable and fails with a "run start.bat first" message otherwise
- [x] 4.3 Create fixtures for registering a uniquely-named user and returning a signed-in `BrowserContext`, plus a two-user fixture yielding two independent contexts
- [x] 4.4 Add an `e2e` script to `FE/package.json`
- [x] 4.5 Write one throwaway smoke test: two contexts, one sends a message, the other receives it

## 5. Scenario traceability tooling

- [x] 5.1 Create `tools/spec-coverage`; implement the parser turning `openspec/specs/**/spec.md` into `<capability>/<requirement-slug>/<scenario-slug>` identifiers
- [x] 5.2 Implement the three result readers — `.trx`, vitest JSON, Playwright JSON — each extracting `@spec:<id>` markers from test display names, normalizing to `{ layer, testName, status, scenarioIds[] }`
- [x] 5.3 Implement the `specTest()` helper for vitest and for Playwright so a claim is written once and prefixed onto the test title
- [x] 5.4 Implement the cross-reference: covered scenarios with their claiming tests and layers, uncovered scenarios grouped by capability, per-capability and overall percentages
- [x] 5.5 Exit non-zero on a claim whose identifier matches no scenario, naming the stale identifier and the test
- [x] 5.6 Treat failed and skipped tests as not covering their claimed scenarios
- [x] 5.7 Write `docs/test-coverage/scenarios.md` plus a console summary; gitignore the generated reports except the committed summary
- [x] 5.8 Verify against the current specs that the parser finds all 270 scenarios across 15 capabilities

## 6. Orchestration

- [x] 6.1 Write `test.bat`: prerequisite checks first, then integration → FE unit → e2e → spec-coverage, each layer's failure reported without aborting the summary
- [x] 6.2 Run ReportGenerator over the coverlet output into `docs/test-coverage/backend/`
- [x] 6.3 Implement `tools/coverage-gate`: read the backend `Summary.txt` and the frontend `coverage-summary.json`, compare both against the 80% threshold, and report the measured percentage and points short for each; `--enforce` makes a shortfall exit non-zero
- [x] 6.4 Call the gate from `test.bat` without `--enforce`, so the shortfall is reported but does not fail the run until per-capability test changes land (design D7, Non-Goals); keep the vitest-level threshold out of `vite.config.ts` for the same reason
- [x] 6.5 Print the summary table: per-layer result, backend and frontend line coverage, scenario coverage, and every report path
- [x] 6.6 Verify each layer is still runnable on its own without the other layers' prerequisites

## 7. Prove the harness on `user-sessions`

- [x] 7.1 Backend integration: sessions are listed most-recently-seen-first with device details; a user never sees another account's sessions; revoking a session belonging to another user is rejected as not found
- [x] 7.2 Backend integration: registration captures device description, user agent, IP, created and last-seen timestamps; revoking marks the session revoked and removes it from the list
- [x] 7.3 Frontend unit: a session is registered on sign-in when no identifier is stored, and on restored login when none is present; the identifier is persisted and sent as `X-Session-Id` on later requests
- [x] 7.4 Frontend unit: session registration failure leaves sign-in successful and the app usable
- [x] 7.5 Frontend unit: the sessions screen marks the current browser's session "Current"
- [x] 7.6 E2E: revoking a non-current session leaves the browser signed in and removes the row; revoking the current session clears credentials and returns to sign-in
- [x] 7.7 Tag every test above with its scenario identifier and confirm `spec-coverage` reports `user-sessions` at 8/8

## 8. Verification and cleanup

- [x] 8.1 Delete the throwaway smoke tests from 2.9, 3.7, and 4.5 now that real tests replace them
- [x] 8.2 Run `test.bat` twice in a row with no manual cleanup and confirm identical results
- [x] 8.3 Confirm no `chat_test_*` database and no development data change survives a full run
- [x] 8.4 Confirm a deliberately misspelled scenario claim fails `spec-coverage` with a clear message, then revert it
- [x] 8.5 Confirm the missing-prerequisite path: stop the stack, run `test.bat`, and check it names what to start before running any tests

## 9. Load and capacity suite

> Deferred to a follow-up change. Sections 1-8 and 10 were executed; this section was not started.

- [ ] 9.1 Create `BE/Tests.Load` (console + NBomber), add to `BE.slnx`, and exclude it from `dotnet test` discovery and from `test.bat`
- [ ] 9.2 Implement a seeding helper that provisions N users, a room with N members, and N messages against the running stack
- [ ] 9.3 Implement the 300-concurrent-connection scenario reporting observed message-delivery and presence-propagation latency against the 3s / 2s targets
- [ ] 9.4 Implement the 1000-member room scenario: posting and member listing succeed, timings reported
- [ ] 9.5 Implement the 10,000-message history scenario: time to first page and to successive older pages
- [ ] 9.6 Implement restart durability: write state, `docker compose restart be`, verify messages, memberships, and moderation state intact
- [ ] 9.7 Write measured-vs-target figures to `docs/test-coverage/load.md`; exit non-zero when a target is missed
- [ ] 9.8 Tag each scenario with its `platform-constraints/*` identifier and confirm it appears in the scenario report as load-layer coverage

## 10. Documentation

- [x] 10.1 Document how to run each layer, the prerequisites, and where reports land — in `CLAUDE.md` and in `BE/CLAUDE.md` / `FE/CLAUDE.md` as appropriate
- [x] 10.2 Document the scenario-claim conventions for all three layers so follow-up per-capability changes have one place to copy from
