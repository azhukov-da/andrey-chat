## Why

The backend integration layer exists but covers exactly one capability: `user-sessions`, 8 of the project's 270 specified scenarios (3.0%). Every server-enforced rule in the other 14 capabilities — who may post in a room, who may ban whom, whether a blocked user can open a direct chat, whether an attachment download refuses a removed member — is currently unverified. The harness, the claim conventions, and the coverage reporting were all built for this and are sitting idle.

## What Changes

- Author backend integration tests for **every scenario the backend layer is accountable for** across the 14 untested capabilities, following the layer-assignment rule already in the `automated-testing` spec: a rule the server enforces regardless of which client calls it is verified here, through real HTTP and real SignalR against a real migrated PostgreSQL database.
- Add one test area per capability under `BE/Tests.Integration/`, each with its own `*Spec` constants class carrying the `@spec:` display-name claims, mirroring the existing `UserSessions` area.
- Extend the shared harness only where the new areas need it (multi-user fixtures, file-upload helpers, hub multi-client assertions) — additively, without changing existing harness behaviour.
- Produce a **layer triage document** naming, per capability, which scenarios were taken by this layer and which belong to the frontend-unit or end-to-end layers, so the remaining coverage work is a known quantity rather than a rediscovery exercise.
- Produce a **spec-gap register** recording every scenario where the implementation diverges from its specification. Application source code is **not** modified to make a test pass; a test that reveals a genuine divergence is committed as a skipped test whose skip reason names the scenario identifier and the observed behaviour, and the gap is logged for a follow-up change.
- **No production code changes.** `BE/Domain`, `BE/Application`, `BE/Infrastructure`, `BE/Web`, and the frontend are read-only for this change.

## Capabilities

### New Capabilities

None. This change adds verification for behaviour that is already specified.

### Modified Capabilities

None. No requirement changes: the `automated-testing` capability already specifies the layers, the traceability scheme, the isolation rules, and the gates. This change implements against those requirements rather than altering them, so it sets `skip_specs: true`.

## Impact

- **Added**: test areas under `BE/Tests.Integration/` — one directory per capability, plus the `*Spec` claim-constant classes.
- **Modified**: `BE/Tests.Integration/Harness/` — additive helpers only. `docs/test-coverage/scenarios.md` regenerates with a substantially higher covered count.
- **Added docs**: `docs/test-layer-triage.md` (which layer owns which scenario) and `docs/spec-gaps.md` (implementation-vs-spec divergences found).
- **Unchanged**: all application source, the frontend, the two coverage-gate tools, `test.bat`.
- **Note**: backend line coverage will rise sharply. It is measured but not enforced (`test.bat` omits `--enforce`); whether 80% is now reachable is an observation this change reports, not a gate it turns on.
