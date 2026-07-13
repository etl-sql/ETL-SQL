# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Central Security Events

### Event contract and emission
- [x] Define a versioned structured security-event schema with stable event ID, severity/type, timestamp, actor/effective identity, host/node, tenant, script/job/correlation IDs, policy version/hash, sanitized target, decision, and reason.
- [x] Emit events for override attempts, denied filesystem/network/connector/process/Docker operations, policy signature/expiry/rollback failures, stale or unavailable policy, machine enrollment changes, and repeated resource-limit violations.
- [x] Separate security events from ordinary diagnostic logs and existing governance audit records while preserving correlation between all three.
- [x] Redact credentials, query parameters, connection strings, environment values, filesystem data, and exception details before persistence or transport.

### Durable delivery and monitoring
- [x] Provide a durable local security-event outbox for every executable, with bounded storage, atomic append, retry, batching, deduplication, jittered backoff, and crash recovery.
- [x] Deliver to an HTTPS/SIEM collector using machine identity; define acknowledgement and idempotency behavior.
- [x] Add Windows Event Log and syslog/structured-file sinks for bootstrap failures that occur before HTTPS delivery is available.
- [x] Support policy-controlled severity filters so enterprises can forward security warnings/denials without centrally shipping all diagnostic logs.
- [x] Add optional fail-closed thresholds for terminal delivery failure, oldest-event age, pending count, and outbox bytes; standalone mode remains local-only by default.
- [x] Expose queue health, last delivery, failures, drops, and collector reachability through diagnostics and fleet status.

### Completion gates
- [x] Fault-injection tests cover collector outage, duplicate delivery, acknowledgement loss, corrupt outbox state, disk pressure, process crash, redaction, and recovery.
- [x] A denial is blocked first and then reported; no enforcement decision depends on successful remote logging unless fail-closed monitoring is explicitly enabled.
- [x] Documentation includes example mappings for common SIEM products without coupling the core event contract to one vendor.

---

## Enterprise Certification & Operations

### Certification lanes
- [x] Add Windows and Linux CI certification lanes for enrollment, signed retrieval, cache/offline
      operation, dynamic refresh, operation enforcement, and security-event delivery.
- [x] Retain per-platform TRX, command logs, and machine-readable/Markdown summaries as CI artifacts.
- [x] Certify Portal, Orchestrator, CLI, TUI, Report Player, Report Builder, Language Server,
      scheduled jobs, spawned runners, and parallel execution.
- [x] Run malicious-input and bypass drills covering policy tampering, stale/expired policy,
      signing-key rotation, machine revocation, path/link races, DNS rebinding, redirect
      re-authorization, connector aliases, Docker escape-oriented options, and log injection.
- [x] Prove standalone regression behavior with no enrollment, no enterprise network calls, and
      unchanged local workflows.

---

## v0.15.0 Release Debt (deferred to ship, fix in v0.16.0)

Findings surfaced during the v0.15.0 release. Full detail in
`Docs/Operations/v0.15.0-flaky-tests.md` and `Docs/Operations/v0.15.0-performance-results.md`.

### Flaky tests — audit the "sleep-then-assert" anti-pattern
- [x] Audited all 84 `Task.Delay` sites. The two genuine CPU-scheduling races (`SchedulerServiceTests`,
      `LinterTests`) are fixed and merged. The rest are legitimate: deadline-based polling loops,
      wall-clock TTL/lease/fencing expiry waits (not CPU-load sensitive), `Task.WhenAny` timeout
      sentinels, and background signal simulators. Full write-up in `Docs/Operations/v0.15.0-flaky-tests.md`.
- [ ] Residual: `StmtPollingTests.TestWaitFor_Cancellation` uses a fixed 2 s delay to ensure the WAITFOR
      polling loop is active before cancelling — robustly fixing it needs the evaluator to expose a
      "waiting" signal (small follow-up, not a delay bump).
- [ ] Consider a lint/CI check that flags a new `Task.Delay(...)` immediately before an assertion in
      test files, to stop the anti-pattern from regrowing.

### Restore the 70% coverage gate
- [ ] `ci.yml`'s coverage threshold was temporarily lowered **70.0 → 69.5** to ship v0.15.0 (it landed
      at 69.8%). **Coverage analysis (2026-07-13):** the v0.15.0 headline feature (`Core.Adaptive.*`)
      is already well-covered (mostly 100%; controller/advisor 100%). The gap is **infrastructure**
      that isn't cheaply unit-tested: Postgres migrations (0%, by design), connectors needing live DBs
      (`MySqlSyntax`, `Neo4jDataSource`), and `App.*` runners (`WarmJobRunner`,
      `EnterpriseEnrollmentManager`, `DatabaseMigrationService`). To restore 70.0: add focused tests
      for the App runners (mockable) and/or bring the least-covered connector logic under test in the
      CI fast-lane scope, verify CI ≥ 70.0, then set the threshold back. (Match CI's coverage scope —
      a local run that excludes Portal reports ~50%, not comparable.)

### Scale-cert performance re-validation
- [x] Re-validated on a quiescent machine (5-sample median): **CONFIRMED regression**, not load noise.
      `ExternalSort_50000_DESC` +30.5% elapsed (462→603 ms), `ExternalAggregate_100000_10grps` GC pause
      +48.6% (14.6→21.7 ms) vs the 2026-07-05 baseline. Detail in `Docs/Operations/v0.15.0-performance-results.md`.
- [x] **Fixed the ExternalSort/spill regression.** The bisect isolated two spill hot-path costs:
      adaptive latency instrumentation ran per row even with adaptive execution disabled, and
      schema-backed rows carrying stable operator markers (`_SYS_SK_*`, `__SET_IDX`) fell back to a
      per-row dictionary snapshot. Spill writes now use allocation-free leases, collect adaptive
      latency only when active, cache stable dynamic layouts, and bulk-write sort runs. A full
      5-sample smoke matrix passes all 13 scenarios; sort recovered 603->550 ms (below the 25% fail
      band) and aggregate GC pause recovered 21.7->0 ms median. The known-good baseline remains
      unchanged so the residual +19% sort warning stays visible.

### Release provenance — gold-standard pre-publish attestation (SECURITY)
- [x] `release.yml` now attests **before** publish and gates it: `attest-provenance` needs the build
      jobs, downloads the still-**draft** release assets (the job carries `contents: write` since a
      draft is invisible with `contents: read` — that was the original "release not found"), attests
      them, and `publish-release` needs `attest-provenance`. A provenance failure fails the workflow
      and leaves the release a **draft**, so no artifact is ever public without provenance.
