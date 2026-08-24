## Why

The end-to-end layer is the only one of the three gated layers that is still essentially empty. [docs/test-layer-triage.md](../../../docs/test-layer-triage.md) assigns nine scenarios to it — the ones whose outcome spans browser, nginx proxy, backend, and database together, and which therefore cannot be observed at either of the other two layers. Only one of those nine is written today. Eight specified behaviours are consequently unverified anywhere in the suite, and the scenario coverage report shows them as gaps with no test that could ever close them at another layer.

## What Changes

- Write the eight missing end-to-end tests, each claiming its triaged `@spec:` scenario identifier, so `docs/test-coverage/scenarios.md` reports every End-to-end row as covered.
- Extend [FE/e2e/fixtures.ts](../../../FE/e2e/fixtures.ts) with the primitives those tests need and the current fixtures lack: signing in with the "keep me signed in" choice, reopening the application in a fresh browser session that keeps persistent storage but discards session storage, creating a room and adding a second account to it, and observing the browser's outbound network requests.
- Verify against the running stack **without modifying application source**. Where a test written to the specification fails because the implementation diverges, the test keeps its spec-accurate assertions, is marked `Skip` with a reason, and the divergence is recorded as a new row in [docs/spec-gaps.md](../../../docs/spec-gaps.md) — the existing convention for exactly this situation.
- Update [docs/testing-conventions.md](../../../docs/testing-conventions.md) with the new fixtures where the end-to-end section describes what the layer offers.

No requirement changes: the `automated-testing` capability already requires cross-cutting flows to be verified at the end-to-end layer and every scenario to be traceable to a claiming test. This change satisfies requirements that already exist rather than adding any, so it declares `skip_specs: true`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This change is test and documentation work against the specifications as they stand.

## Impact

- **Added**: end-to-end test files under [FE/e2e/](../../../FE/e2e/) covering `authentication`, `chat-rooms`, `chat-ui-shell`, `platform-constraints`, and `realtime-delivery`.
- **Modified**: [FE/e2e/fixtures.ts](../../../FE/e2e/fixtures.ts) (new fixtures and helpers), [docs/testing-conventions.md](../../../docs/testing-conventions.md), and [docs/spec-gaps.md](../../../docs/spec-gaps.md) if divergences surface.
- **Not modified**: any file under `FE/src/`, `BE/`, `docker-compose.yml`, or `FE/nginx.conf`. A failing test is evidence about the implementation, not a licence to change it.
- **Runtime**: the gated end-to-end run grows from 2 tests to 10. The two multi-user reconnection and deletion tests are the slowest; the suite's 60s per-test timeout and 2 workers stay as they are.
- **Prerequisite unchanged**: the whole stack serving on `http://localhost:3000` via `start.bat`. Two of the new tests additionally read the Docker Compose topology from the host, which needs the `docker` CLI available to the test process.
