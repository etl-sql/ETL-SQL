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

## v0.15.0 Release Debt (deferred to ship, fix in v0.16.0)

Findings surfaced during the v0.15.0 release. Full detail in
`Docs/Operations/v0.15.0-flaky-tests.md` and `Docs/Operations/v0.15.0-performance-results.md`.

### Flaky tests — audit the "sleep-then-assert" anti-pattern
- [ ] Sweep the suite for `await Task.Delay(...)` immediately followed by an assertion that an async
      action *did* happen (a race). ~84 occurrences across 39 files, concentrated in the orchestration
      and portal timing suites (ExecutionJobService 7, JobScheduling 7, NodeRegistry 6, Subscription 5,
      OrchestratorPostgresStore 5, …). Convert to poll-for-condition — reference patterns:
      `SchedulerServiceTests.WaitUntilAsync` and `LinterTests` `ConcurrencyTracker` (both shipped in
      v0.15.0). `Times.Never()` bounded-sleep observation windows and deliberate slow-op holds are fine.
- [ ] Consider a lint/CI check that flags a new `Task.Delay(...)` immediately before an assertion in
      test files, to stop the anti-pattern from regrowing.

### Restore the 70% coverage gate
- [ ] `ci.yml`'s coverage threshold was temporarily lowered **70.0 → 69.5** to ship v0.15.0 (it landed
      at 69.8% as the release's large new surface outpaced tests). Add targeted tests for the
      least-covered new v0.15.0 code and restore the threshold to **70.0**.

### Scale-cert performance re-validation
- [ ] Smoke/standard scale certification was skipped (`-SkipScale`) for the v0.15.0 gate: the
      single-sample micro-benchmarks tripped their 25% bands unpredictably under concurrent machine
      load (the failing scenario moved run-to-run on identical code; peak-working-set inflated across
      unrelated scenarios). Re-run **multi-sample median on a quiescent machine**; if `ExternalSort` /
      `ExternalAggregate` are genuinely regressed vs the 2026-07-05 baseline, bisect and fix, then
      re-bless `certification-results/baseline-smoke.json` from the clean run.

### Release provenance — gold-standard pre-publish attestation (SECURITY)
- [ ] v0.15.0 shipped **without** SLSA provenance: the `attest-provenance` job ran before publish and
      failed downloading a still-draft release, and (being a dependency of publish) skipped publishing.
      Interim fix reordered it after publish and removed `continue-on-error` so failures are visible.
      **Proper fix:** attest the build jobs' `upload-artifact` outputs *before* publish and keep the
      release a **draft** until provenance succeeds (artifacts never public without provenance); a small
      finalize job then flips `--draft=false`. Flagged by the commit security review.
