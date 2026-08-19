## Purpose

How the chat system's specified behavior is verified automatically: the layers of tests and what each is accountable for, how every scenario in the specs is traced to the tests that exercise it, what the coverage gates are and what they deliberately exclude, and how a developer runs the whole thing.

## ADDED Requirements

### Requirement: Layered verification

The system's behavior SHALL be verified by three gated test layers, each accountable for a distinct class of specified behavior:

- **Backend integration** — server-enforced rules, observed through the same HTTP and real-time interfaces a client uses, against a real relational database with production schema migrations applied.
- **Frontend unit and component** — client-enforced rules and rendering, observed through the user-facing component tree with backend transports substituted.
- **End-to-end** — cross-cutting flows that only the assembled system can demonstrate, observed through a real browser against the running stack.

A scenario SHALL be assigned to the layer that can observe its outcome most directly. A scenario whose outcome is observable at more than one layer SHALL NOT be required to appear at every layer.

#### Scenario: Server-enforced rule

- **WHEN** a specified rule is enforced by the backend regardless of which client calls it
- **THEN** it is verified at the backend integration layer, exercising the real HTTP or real-time interface rather than calling internal services directly

#### Scenario: Client-enforced rule

- **WHEN** a specified rule governs what the user interface shows, enables, or blocks before contacting the backend
- **THEN** it is verified at the frontend unit and component layer with the backend transport substituted

#### Scenario: Cross-cutting flow

- **WHEN** a specified behavior spans browser, frontend, backend, and database together — such as one user's action becoming visible in another user's open session
- **THEN** it is verified at the end-to-end layer against the running stack, using separate browser sessions for each participant

#### Scenario: Real-time behavior at the integration layer

- **WHEN** a specified behavior requires one connected client to observe an event caused by another
- **THEN** the backend integration layer SHALL be able to connect multiple authenticated real-time clients simultaneously and assert on the events each receives

### Requirement: Scenario traceability

Every scenario declared in the project's capability specifications SHALL have a stable identifier derived from its capability, requirement, and scenario name. Each automated test SHALL declare the identifiers of the scenarios it verifies, and the verification tooling SHALL produce a report mapping every specified scenario to the tests that claim it.

Scenario coverage SHALL be reported and tracked separately from code coverage, and SHALL NOT be conflated with it.

#### Scenario: Covered scenario

- **WHEN** at least one passing test declares a scenario's identifier
- **THEN** the report marks that scenario as covered and names the tests and layers that cover it

#### Scenario: Uncovered scenario

- **WHEN** no test declares a scenario's identifier
- **THEN** the report lists that scenario as uncovered under its capability, and the summary states the covered count and percentage per capability

#### Scenario: Test claiming an unknown scenario

- **WHEN** a test declares an identifier that matches no scenario in the specifications
- **THEN** the report flags it as a stale or misspelled claim and the tooling exits with a failure

#### Scenario: Specification changes

- **WHEN** a scenario is added, renamed, or removed in the specifications
- **THEN** the next report reflects the change without any edit to the tooling, because identifiers are derived from the specification files themselves

#### Scenario: Failing test does not count

- **WHEN** a test declaring a scenario identifier fails or is skipped
- **THEN** that scenario is not counted as covered

### Requirement: Code coverage gate

Backend and frontend line coverage SHALL each be measured and reported, and the verification run SHALL fail when either falls below 80 percent. Coverage SHALL be measured from the same execution that verifies the scenarios, so that the reported number reflects behavior actually exercised through public interfaces.

Generated code, test projects, and the test tooling itself SHALL be excluded from the measured denominator. The end-to-end and load layers SHALL NOT contribute to the measured coverage.

#### Scenario: Coverage below threshold

- **WHEN** backend or frontend line coverage is below 80 percent
- **THEN** the verification run reports the shortfall with the measured percentage and exits with a failure

#### Scenario: Coverage at or above threshold

- **WHEN** both backend and frontend line coverage are at or above 80 percent and all tests pass
- **THEN** the verification run exits successfully and writes a human-readable coverage report

#### Scenario: Exclusions are declared

- **WHEN** a file or directory is excluded from coverage measurement
- **THEN** the exclusion is declared in configuration rather than applied ad hoc, and appears in the generated report

### Requirement: Test isolation

Each backend integration test group SHALL run against its own database, created fresh and migrated using the same migration path the application uses at startup, and removed when the group finishes. Tests within a group SHALL start from a known-empty state.

Automated tests SHALL NOT read, modify, or delete data belonging to the development database, and SHALL NOT depend on data left behind by a previous run or by manual use of the application.

#### Scenario: Independent runs

- **WHEN** the full verification suite is run twice in a row without any manual cleanup
- **THEN** both runs produce identical results

#### Scenario: Development data untouched

- **WHEN** the verification suite runs while the developer has data in the application
- **THEN** that data is unchanged afterwards

#### Scenario: Order independence

- **WHEN** tests are executed in a different order or a single test is executed alone
- **THEN** results are unchanged

#### Scenario: Aborted run

- **WHEN** a test run is interrupted before completing
- **THEN** the next run succeeds without manual cleanup, reclaiming or bypassing anything the interrupted run left behind

#### Scenario: Database unavailable

- **WHEN** the database the tests depend on is not reachable
- **THEN** the run fails immediately with a message naming the expected database endpoint and how to start it, rather than failing test by test

### Requirement: Load and capacity verification

The capacity, latency, and durability figures stated in the platform constraints specification SHALL be verified against the assembled system at their stated magnitudes, by a suite run on demand rather than as part of the gated verification run. Its results SHALL be reported as measured figures compared against the stated targets, and it SHALL NOT contribute to code coverage.

#### Scenario: Concurrent connections

- **WHEN** the load suite establishes 300 simultaneous real-time client connections and exchanges messages
- **THEN** it reports observed message delivery and presence propagation latencies against the specified targets

#### Scenario: Large room

- **WHEN** the load suite creates a room holding 1000 members
- **THEN** posting to it and listing its members succeed, and the observed timings are reported

#### Scenario: Large history

- **WHEN** the load suite seeds a room with more than 10,000 messages
- **THEN** it reports the time to load the most recent page and successive older pages

#### Scenario: Restart durability

- **WHEN** the load suite restarts the backend after writing messages, memberships, and moderation state
- **THEN** it verifies that state is intact afterwards

#### Scenario: Target not met

- **WHEN** a measured figure falls short of its specified target
- **THEN** the suite reports the gap explicitly and exits with a failure, without affecting the gated verification run

### Requirement: Running the suites

A developer SHALL be able to run the gated verification — all three layers, both coverage reports, and the scenario traceability report — with a single command, and SHALL also be able to run each layer individually for a faster feedback loop. Verification SHALL be runnable locally without depending on a hosted continuous integration service.

The command SHALL state its prerequisites and SHALL report which layers ran, which passed, and where the generated reports were written.

#### Scenario: Full run

- **WHEN** the developer runs the single verification command with prerequisites satisfied
- **THEN** all three gated layers run, both coverage gates are evaluated, the scenario report is regenerated, and a summary names each layer's result and each report's location

#### Scenario: Single layer

- **WHEN** the developer runs one layer on its own
- **THEN** only that layer executes, and its result is reported without requiring the other layers' prerequisites

#### Scenario: Missing prerequisite

- **WHEN** a prerequisite for a layer is not satisfied
- **THEN** the command reports which prerequisite is missing and what to do about it, before running any tests

#### Scenario: Test failure reporting

- **WHEN** a test fails
- **THEN** the summary identifies the failing test, its layer, and the scenario identifiers it declared
