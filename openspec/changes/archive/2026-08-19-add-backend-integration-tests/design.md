## Context

The harness in [BE/Tests.Integration/Harness/](../../../BE/Tests.Integration/Harness/) already provides everything a backend test needs structurally: a per-collection `chat_test_<guid>` database migrated through the application's own startup path, Respawn truncation between tests, `CreateUserAsync()` for a registered and signed-in user with an `HttpClient` carrying `Authorization` and `X-Session-Id`, and `ConnectHubAsync(user)` for an authenticated SignalR client with `Expect<T>(name)` claim-before-trigger event assertions. `UserSessions/SessionsTests.cs` is the worked example. The claim convention is documented in [docs/testing-conventions.md](../../../docs/testing-conventions.md).

What is missing is breadth: 14 capabilities, 262 scenarios, none of them touched. See proposal.md — Why.

Two constraints shape everything below:

1. **Application source is read-only.** A test may not be made to pass by changing the behaviour it verifies.
2. **A scenario belongs to exactly the layer that observes it most directly** — the `automated-testing` spec's layer-assignment requirement. Most of `chat-ui-shell`, and the recording/playback halves of `attachments` and `speech-to-text`, are not backend-observable at all.

## Goals / Non-Goals

**Goals:**

- Backend integration tests for every scenario in the 14 untested capabilities that names a server-enforced rule.
- A written, per-scenario triage of all 262 remaining scenarios, so the frontend and e2e work left over is enumerated rather than guessed at.
- Additive-only harness growth: existing tests keep passing unchanged.
- A green suite at the end of the change, with every implementation-vs-spec divergence recorded rather than silently accommodated.

**Non-Goals:**

- Frontend unit tests and e2e tests. Named in the triage, written by a later change.
- The load and capacity layer. Specified but deliberately outside the gated run; the `platform-constraints` capacity scenarios stay uncovered here.
- Turning on the line-coverage gate (`--enforce`). Coverage will move a lot; deciding the gate is a separate call.
- Fixing any defect this work uncovers.

## Decisions

### One test area per capability, mirroring `UserSessions/`

`BE/Tests.Integration/<Capability>/` in PascalCase (`RoomModeration/`, `ContactsAndBlocking/`, ...), each with one or more `*Tests.cs` classes, split by requirement when a capability is large. The `*Spec` claim-constant class for each capability goes in `Harness/SpecClaims/<Capability>Spec.cs` rather than piling into `IntegrationTest.cs`, which currently carries `UserSessionsSpec` only because it was the first.

*Alternative considered:* one file per requirement in a flat directory. Rejected — the capability is the unit the coverage report groups by, so matching it keeps "what is left" answerable from the directory tree.

`UserSessionsSpec` stays where it is. Moving it is churn in a file the change otherwise does not touch.

### A layer triage document, written before the tests

`docs/test-layer-triage.md`: one table per capability, one row per scenario, columns *scenario identifier* / *layer* / *why*. Produced first, from the spec text, and reviewed as its own task. It is the change's work-list — the tests are written against it — and it survives the change as the standing answer to "why does the backend not cover X".

*Alternative considered:* deciding the layer inline while writing each test. Rejected — it makes the omissions invisible, which is the exact failure mode the traceability requirement exists to prevent.

### Rough allocation, firmed up by the triage task

| Capability | Backend layer takes | Left to FE / e2e |
|---|---|---|
| `authentication` | credential outcomes, bearer enforcement, refresh acceptance and rejection, password change and reset, per-session sign-out | "keep me signed in" storage, absence of idle logout |
| `user-accounts` | registration, uniqueness, username format and immutability, display name, profile read, deletion | — |
| `chat-rooms` | creation, name uniqueness including reuse after delete, catalog and search visibility, join and refusal paths, leave, owner-only settings and deletion | leave-control visibility, private-rooms screen |
| `messaging` | send authority, size limit, replies, edit and delete authority, history order and paging, history access control | composer warning, cancelling a reply |
| `direct-messages` | friendship precondition, reopening, self and blocked refusals, frozen-chat read vs. write, listing | sidebar rendering |
| `room-moderation` | the whole capability — roles, admin authority, promote and demote, ban as removal, ban list, unban, loss of access, invitations and responses | role display, admin action affordances |
| `contacts-and-blocking` | requests, accept and reject, removal, blocking effects, unblocking | contact-list rendering |
| `attachments` | upload authority, per-kind size limits, metadata, download access control, storage layout, transcription outcomes and the length limit | paste, inline rendering, recording, playback |
| `realtime-delivery` | connect and authorize, group scoping and fan-out, event kinds, own-message echo, refused operations surfacing as errors | reconnect behaviour, latency, browser-side dedup |
| `unread-notifications` | unread bump and clear, read position per membership, mark-read authority, typing broadcast, friend-request and invitation pushes | badge rendering, aggregate display |
| `user-presence` | states over the hub, heartbeat transitions, broadcast to interested clients, bulk lookup | multi-tab leadership, indicator rendering |
| `speech-to-text` | endpoint authorization, empty or unreadable audio, service-unavailable handling, oversized clip | dictation UX, permission denial |
| `platform-constraints` | durable persistence across restart, startup migration, configured file-storage root, immediate effect of ban and promotion | proxy boundary (e2e), capacity (load layer, not built) |
| `chat-ui-shell` | unauthenticated access to protected API resources, only | essentially the whole capability |

### External seams are substituted in the factory, not stubbed in the app

`ITranscriptionService` gets a test double registered in `ChatAppFactory`'s service overrides, and `ITranscriptionJobQueue` / `TranscriptionBackgroundService` get a deterministic drain so a transcription assertion never races a background worker. Both are test-project registrations against interfaces the application already exposes — no application change.

*Alternative considered:* running the real `FasterWhisperTranscriptionService`. Rejected — it makes the suite depend on an external service, which breaks both the isolation requirement and the "runnable locally without a hosted CI" requirement.

### A spec gap is a skipped test plus a register entry

When a test written honestly against the spec fails because the implementation diverges:

1. The test keeps its spec-accurate assertions.
2. It is marked `[Fact(Skip = "...")]` with a reason naming the scenario identifier and the observed behaviour.
3. A row goes into `docs/spec-gaps.md`: identifier, expected, observed, and the source location that produces the observed behaviour.

The coverage report then shows the scenario as claimed-but-not-passing, which is exactly what it is — more informative than either a red suite or a test rewritten to bless the bug. `docs/spec-gaps.md` becomes the input to a follow-up change.

*Alternatives considered:* leaving the tests failing — makes `test.bat` permanently red, so it stops being a signal; asserting the actual behaviour — turns the spec claim into a lie and buries the defect.

### Determinism over sleeping

Real-time assertions claim the event with `Expect<T>` before triggering it, as the conventions require. Where a behaviour is genuinely time-based — the presence idle threshold, the transcription length limit — the test drives the threshold through configuration supplied by the factory rather than waiting real seconds.

## Risks / Trade-offs

- **Volume.** Roughly 150 backend tests is a large single change → sequenced capability by capability in tasks.md, each capability independently mergeable and independently green.
- **The triage is a judgment call, and a wrong call silently drops a scenario** → the triage is a reviewable artefact with a stated reason per row, and the coverage report keeps every untaken scenario visibly uncovered.
- **The spec-gap register could become a dumping ground for tests that are simply wrong** → an entry requires pointing at the source location that produces the observed behaviour; a gap that cannot be located that precisely is a test bug, not an implementation gap.
- **Harness additions could destabilise the existing `user-sessions` tests** → additions only, no signature changes, and the existing tests run in every capability's verification step.
- **Line coverage may still miss 80 percent** even at this breadth, because not every error path and infrastructure wiring is reachable through public interfaces. Reported, not gated.
- **Runtime grows.** A per-collection database and real hub connections are not cheap. Capability areas get their own xUnit collections so they parallelise; if wall-clock becomes a problem the answer is more collections, not fewer tests.

## Open Questions

- Whether backend line coverage reaches 80 percent once this lands, and therefore whether `test.bat` can adopt `--enforce`. Answerable only after the tests exist, and it changes neither the approach nor the task breakdown.

## Answer to the open question

Backend line coverage reached **87.0%** with this change's tests in place, against the 80 percent
threshold — measured by `tools/coverage-gate` from the cobertura the platform's code-coverage
extension writes, with the exclusions in `BE/codecoverage.runsettings`. The backend half of the gate
could therefore be enforced today.

`test.bat` still calls `tools/coverage-gate` without `--enforce`, because the gate is one number
across both layers and the frontend is at 6.9% until its own layer is written. Turning it on is the
frontend change's call, not this one's.
