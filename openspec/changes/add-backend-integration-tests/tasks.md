## 0. Unblock the runner

Not in the original plan. The backend layer could not run at all: the .NET 10 SDK refuses to execute
a Microsoft.Testing.Platform test project through the VSTest target, which is how `test.bat` drove
it. Pre-existing, and blocking every task below. Approved as an addition to the change's scope.

- [x] 0.1 Select the platform runner: `global.json` with `"test": { "runner": "Microsoft.Testing.Platform" }`, plus `UseMicrosoftTestingPlatformRunner` and `TestingPlatformDotnetTestSupport` in the test csproj.
- [x] 0.2 Replace `coverlet.collector` — a VSTest data collector, unreachable from the platform — with `Microsoft.Testing.Extensions.TrxReport` 2.3.3 and `Microsoft.Testing.Extensions.CodeCoverage` 18.9.0, versioned to match the platform xunit.v3 brings in.
- [x] 0.3 Port `BE/coverlet.runsettings` to `BE/codecoverage.runsettings` with the same exclusion set, and record the rationale in `BE/CLAUDE.md` because the settings parser rejects a file containing XML comments.
- [x] 0.4 Rewrite `test.bat`'s backend invocation to pass reporting arguments after a bare `--`, with an absolute `--coverage-settings` path.
- [x] 0.5 Confirm the artefacts still land where the gates read them: `tools/spec-coverage` parses the platform's trx, and ReportGenerator consumes the cobertura.

## 1. Triage and harness groundwork

- [x] 1.1 Write `docs/test-layer-triage.md`: every specified scenario, one row each, assigned to backend / frontend-unit / e2e / load, with a one-line reason. Cross-check the row count against the scenarios `tools/spec-coverage` derives so nothing is dropped.
- [x] 1.2 Create `docs/spec-gaps.md` with its header and empty table (identifier, expected, observed, source location); it fills in as the capabilities land.
- [x] 1.3 Add `BE/Tests.Integration/Harness/SpecClaims/` and move nothing — `UserSessionsSpec` stays in `IntegrationTest.cs`. Add a README-comment in the folder stating the one-class-per-capability rule.
- [x] 1.4 Extend the harness additively with REST convenience helpers used by more than one capability: create room, join room, invite, send message, upload attachment, friend two users. No changes to existing signatures.
- [x] 1.5 Add a per-capability xUnit collection so capability areas parallelise, and confirm the existing `user-sessions` tests still pass unchanged: `dotnet test BE/Tests.Integration/Tests.Integration.csproj`.

## 2. Identity and account capabilities

- [x] 2.1 `UserAccounts/` — registration, missing field, username and email uniqueness, username format, username-equals-email, no rename path, display name set and absent, own-profile read, unauthenticated profile request, deletion and double deletion. Add `UserAccountsSpec`.
- [x] 2.2 `Authentication/` — valid and invalid credentials, repeated failures, missing token, public catalog exception, expired access token refresh, refresh rejection, sign-out scoped to the current browser. Add `AuthenticationSpec`.
- [x] 2.3 `Authentication/` — password change (success, wrong current password, weak new password) and password reset (request, completion, invalid code).
- [x] 2.4 Run the two areas green, log any divergence into `docs/spec-gaps.md` per the design's skip-plus-register rule.

## 3. Rooms and messaging

- [x] 3.1 `ChatRooms/` — creation and missing name, name uniqueness on create and rename, reusing a deleted room's name, room read as member and as non-member of a public room. Add `ChatRoomsSpec`.
- [x] 3.2 `ChatRooms/` — public catalog browse and search, private rooms hidden, already-a-member marking, joining, banned-from-room refusal, already-a-member join, private-room join attempt, non-member private-room request.
- [x] 3.3 `ChatRooms/` — member leaves, owner refused leave, owner saves settings, admin refused settings, owner deletes, non-owner refused deletion, messaging a deleted room.
- [x] 3.4 `Messaging/` — member sends text, multiline and emoji preserved, non-member refused, banned member refused, oversized send refused. Add `MessagingSpec`.
- [x] 3.5 `Messaging/` — replies: composing, quoting a deleted message, quoting an attachment message, reply target in another room refused.
- [x] 3.6 `Messaging/` — author edits, non-author refused, editing a deleted message; author deletes, admin deletes another user's message, unauthorized delete refused, deleting twice.
- [x] 3.7 `Messaging/` — history in chronological order, offline recipient receives on next read, incremental paging back, reaching the beginning, non-member refused history.
- [x] 3.8 Run both areas green, log divergences.

## 4. Direct messages, contacts, moderation

- [x] 4.1 `ContactsAndBlocking/` — viewing contacts, outgoing requests not listed as contacts, request by username, request with a message, unknown target, duplicate request, self request. Add `ContactsAndBlockingSpec`.
- [x] 4.2 `ContactsAndBlocking/` — accepting, rejecting, sender cannot self-accept, removing a friend, blocking, blocked user refused write, history stays readable, blocking oneself refused, unblocking, unblocking someone not blocked.
- [x] 4.3 `DirectMessages/` — feature parity with rooms, no moderation roles, not in the catalog, first message to a friend, reopening, not-friends refusal, blocked-either-direction refusal, chat with self refused, frozen chat write refused and read allowed, unblocking restores messaging, listing. Add `DirectMessagesSpec`.
- [x] 4.4 `RoomModeration/` — owner privileges permanent, member and outsider refused moderation, promoting, demoting, promoting an existing admin, target not a member. Add `RoomModerationSpec`.
- [x] 4.5 `RoomModeration/` — removal is a ban, removed user cannot rejoin, admin removing an admin, banning with a reason, banning an already banned user, admin banning an admin, viewing bans, unbanning, unbanning someone not banned.
- [x] 4.6 `RoomModeration/` — reading history after removal refused, downloading an attachment after removal refused, sending an invitation, inviting a banned user, duplicate invitation, non-admin refused invite, accepting, rejecting, answering someone else's invitation, answering twice, invitation to a deleted room, banned before accepting.
- [x] 4.7 Run the three areas green, log divergences.

## 5. Real-time, unread, presence

- [x] 5.1 `RealtimeDelivery/` — connection after sign-in, unauthenticated connect refused, group subscriptions on connect. Add `RealtimeDeliverySpec`. (Sign-out dropping the connection is client lifecycle; the triage puts it at the frontend layer.)
- [x] 5.2 `RealtimeDelivery/` — room message fan-out to two connected clients, joining mid-session receives subsequent messages, personal notification scoping, delivery inside the three-second budget, refused send surfaces as a `HubException`. (Applying an arriving edit, deletion, or membership change in place is view behaviour; the triage puts those at the frontend layer.)
- [x] 5.3 `UnreadNotifications/` — unread bump for a background chat, opening clears the count, read position recorded per membership, mark-read in a chat one is not in refused, typing broadcast and stop-on-send, friend-request push. Add `UnreadNotificationsSpec`. (Badge rendering, the aggregate count, the HTTP fallback, and the invitations sidebar are frontend per the triage.)
- [x] 5.4 `UserPresence/` — connected-and-active, connected-but-idle, no-connection states; heartbeat driving idle and return-from-idle with the threshold supplied through test configuration; multi-connection aggregation (online while any connection remains, offline when the last ends); presence change broadcast on connect and disconnect; bulk `GetPresenceFor` lookup. Add `UserPresenceSpec`.
- [x] 5.5 Run the three areas green, log divergences.

## 6. Attachments and speech-to-text

- [x] 6.1 Register the `ITranscriptionService` double and the deterministic `ITranscriptionJobQueue` drain in the test factory's service overrides; verify nothing reaches the real `FasterWhisperTranscriptionService` during a run.
- [x] 6.2 `Attachments/` — uploading a file, several files at once, non-member refused, banned uploader refused, empty file refused, oversized image and oversized file refused per kind. Add `AttachmentsSpec`.
- [x] 6.3 `Attachments/` — original name preserved, comment on an attachment, member downloads, non-member refused, uploader who lost access refused, unknown attachment, file survives loss of access, storage layout under the configured root.
- [x] 6.4 `Attachments/` — voice attachment gets a transcript, transcription failure handled, long voice attachment uploads without a transcript.
- [x] 6.5 `SpeechToText/` — successful transcription, unauthenticated request refused, empty or unreadable audio, transcription service unavailable, oversized clip submitted directly. Add `SpeechToTextSpec`.
- [x] 6.6 Run both areas green, log divergences.

## 7. Platform constraints and close-out

- [x] 7.1 `PlatformConstraints/` — durable persistence across a restart of the application host, startup migration applied by the application's own path, configured file-storage root honoured, ban takes effect immediately, promotion takes effect immediately. Add `PlatformConstraintsSpec`. Note capacity and proxy scenarios as out of layer, per the triage.
- [x] 7.2 `ChatUiShell/` — no test area. The triage puts all 27 scenarios at the frontend or end-to-end layer: route guarding is decided by the router before any request is made, and the API's own refusal of an unauthenticated caller is already claimed by `authentication/bearer-token-authorization`. Record the reasoning and move on.
- [x] 7.3 Run the full backend layer twice in a row with no cleanup between runs and confirm identical results, and confirm a single test run in isolation gives the same verdict — the isolation and order-independence requirements.
- [x] 7.4 Run `test.bat`. Confirm `tools/spec-coverage` reports no unknown-scenario claims, and record the new covered count per capability.
- [x] 7.5 Finalise `docs/spec-gaps.md` and `docs/test-layer-triage.md`; add a short pointer to both from the Automated tests section of the root `CLAUDE.md`.
- [x] 7.6 Confirm the diff touches no application source: `git diff --stat -- BE/Domain BE/Application BE/Infrastructure BE/Web FE/src` must be empty.
- [x] 7.7 Report whether backend line coverage reached 80 percent, answering design.md's open question. Do not enable `--enforce` as part of this change.
