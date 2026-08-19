## Why

The application has 270 specified scenarios across 15 capabilities and zero automated tests — every regression is found by hand, and nothing connects the specs in `openspec/specs/` to evidence that the system actually behaves that way. Testing all 270 scenarios at once is too large for one change, and doing it capability-by-capability without a harness first means the harness gets redesigned mid-stream.

This change builds the test infrastructure and proves it end to end on one thin capability, so the 14 remaining capabilities become mechanical test-authoring work rather than infrastructure work.

## What Changes

- **New backend integration test project** (`BE/Tests.Integration`) hosting the app in-process via `WebApplicationFactory<Program>`, exercising REST, ASP.NET Identity, and the SignalR hub against a real PostgreSQL database, with a unique database per test collection.
- **New frontend unit/component test setup** in the existing vitest install: jsdom environment, Testing Library, MSW request mocking, and a fake hub transport that lets SignalR event handlers be driven directly.
- **New Playwright end-to-end suite** (`FE/e2e`) driving the running Docker Compose stack at `http://localhost:3000`, including multi-context tests for real-time and presence behavior.
- **New on-demand load test project** (`BE/Tests.Load`, NBomber) that measures the `platform-constraints` numbers for real — 300 concurrent connections, a 1000-member room, a 10,000-message history, and restart durability. Deliberately outside the coverage gate.
- **New scenario-traceability tool** (`tools/spec-coverage`) that parses `openspec/specs/**/spec.md` into scenario identifiers, cross-references them against machine-readable test results from all three gated layers, and reports which specified scenarios have no test.
- **Two coverage gates, tracked separately**: spec-scenario coverage (drives *what* to test) and code line coverage at 80% for backend and frontend (the enforced gate).
- **One-command local entry point** (`test.bat`) that runs the gated layers and produces both reports. No CI workflow — the gate is run locally and on demand.
- **Proof-of-harness tests for `user-sessions`** (8 scenarios), the thinnest capability, written across all three layers to validate every part of the harness before the remaining capabilities are attempted.
- Supporting changes: a new `docker-compose.tests.yml` overlay that publishes the `db` port as `55432:5432` so host-run tests can reach it — kept out of the base compose file so the deployment topology `platform-constraints` specifies is unchanged — and `public partial class Program { }` appended to `BE/Web/Program.cs` so the web host is addressable by the test factory.

Out of scope: test authoring for the other 14 capabilities. Each becomes its own follow-up change, and the 80% line-coverage gate is only expected to be *met* once those land — this change establishes the measurement, the tooling, and the first capability.

## Capabilities

### New Capabilities
- `automated-testing`: How the system's behavior is verified — the test layers and what each is responsible for, how specified scenarios are traced to tests, what the coverage gates are and what they exclude, how test data is isolated, and how the suites are run.

### Modified Capabilities
<!-- None. No existing capability's required behavior changes; the docker-compose port and the Program.cs marker are implementation details serving the new capability. -->

## Impact

- **New projects**: `BE/Tests.Integration`, `BE/Tests.Load` (both added to `BE.slnx`), `FE/e2e`, `tools/spec-coverage`.
- **Modified production code**: `BE/Web/Program.cs` (partial class marker only, no behavior change). `docker-compose.yml` is **not** modified; the test-only port lives in a new `docker-compose.tests.yml`.
- **Modified frontend config**: `FE/vite.config.ts` gains a vitest configuration block with coverage thresholds; `FE/package.json` gains test scripts and dev dependencies (`@vitest/coverage-v8`, `@playwright/test`, `jsdom`).
- **New backend dependencies**: `Microsoft.AspNetCore.Mvc.Testing`, `xunit.v3`, `Respawn`, `Npgsql`, `coverlet.collector`, `NBomber`.
- **New generated artifacts**: `docs/test-coverage/` (scenario and line coverage reports), gitignored except for a committed summary.
- **Operational**: backend integration tests require `docker compose up db` to be running; end-to-end tests require the full stack. Test databases are created and dropped on the same PostgreSQL server that holds the development `chat` database.
