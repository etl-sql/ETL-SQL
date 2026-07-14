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
- [x] Fixed `StmtPollingTests.TestWaitFor_Cancellation` (test-only). The real issue was the exact-type
      assertion: cancellation surfaces as `TaskCanceledException` (loop `Task.Delay`) or the base
      `OperationCanceledException` (loop token check) depending on where it lands. Switched to
      `Assert.ThrowsAnyAsync<OperationCanceledException>`, which accepts either and removes the timing
      dependency — the 2 s delay drops to 100 ms and no evaluator "waiting" signal is needed. Verified
      green across 3 consecutive runs.
- [x] Added `scripts/check-flaky-test-delays.mjs` — flags `await Task.Delay(<literal>)` that is the
      sole sync before a positive assertion (excludes polling loops, `Task.WhenAny` sentinels, and
      negative/absence assertions). Wired as a blocking CI step. The 8 current wall-clock TTL/timing
      waits are annotated `// flaky-delay-ok: <reason>`; new un-annotated violations fail CI.

### Restore the 70% coverage gate
`ci.yml`'s threshold was lowered **70.0 → 69.5** to ship v0.15.0 (landed at 69.8%). **Analysis
(2026-07-13):** the v0.15.0 headline feature (`Core.Adaptive.*`) is already well-covered (mostly
100%). The gap is infrastructure: Postgres migrations (0%, by design), some connectors, and `App.*`
runners.
- [x] Added `MySqlSyntaxTests` (pure dialect vocabulary — was 0%). A genuine down-payment / pattern
      for the pure-logic connector classes.
- [ ] `App.*` runners (`WarmJobRunner`, `EnterpriseEnrollmentManager`, `DatabaseMigrationService`) are
      the biggest untested chunk but hardcode elevation checks + stores + file I/O — meaningful tests
      need a testability seam (inject the store / elevation predicate) first, not error-path-only tests.
- [ ] Iterate CI-in-the-loop: add tests → push → read the CI coverage % (the authoritative scope;
      a local run excluding Portal reports ~50%, not comparable) → repeat until ≥ 70.0, then restore
      the `ci.yml` threshold to **70.0**.

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
- [x] **Residual sort gap resolved** via a `CompareConstants` hot-path optimization (same-type fast
      path + `TryParse` instead of `Parse`-in-`try/catch`, eliminating 4 `ToString` + 2 `decimal.Parse`
      + a per-call exception per comparison). `ExternalSort_50000_DESC` **550 -> 212 ms** — now ~2.2x
      *faster* than the 462 ms baseline, so the scale-cert gate is reliably green with margin.
      Validated: scale cert 13/13, full fast lane green, no failing regressions.

### Release provenance — gold-standard pre-publish attestation (SECURITY)
- [x] `release.yml` now attests **before** publish and gates it: `attest-provenance` needs the build
      jobs, downloads the still-**draft** release assets (the job carries `contents: write` since a
      draft is invisible with `contents: read` — that was the original "release not found"), attests
      them, and `publish-release` needs `attest-provenance`. A provenance failure fails the workflow
      and leaves the release a **draft**, so no artifact is ever public without provenance.

---

## Enterprise Policy Hardening

Brought in from `ROADMAP.md` → *Enterprise Policy Enforcement & Monitoring → Phase 1: Policy
Hardening*. **Verified against the code 2026-07-13:** three of the four roadmap bullets already
shipped opportunistically, so this phase is now weighted toward **canary policy rollout** plus a
couple of close-out/audit tasks. Per-bullet status is annotated below. Once this phase is committed,
mark the corresponding shipped roadmap lines done (or move them to `CHANGELOG.md`).

### Race-resistant filesystem mutation — close residual gaps
Already shipped: `FileHandleFinalPath` (Win `GetFinalPathNameByHandle` / Linux `/proc/self/fd`) +
`FileSystemPolicyAuthorizer.{Delete,Move}Validated{File,Directory}` perform a handle-based final-path
re-check between authorize and mutate; `FileOperationStatementHandler` routes local DELETE/MOVE/RENAME
through them; link/junction substitution tests live in `FileSystemPolicyAuthorizerTests` and
`HardeningSecurityTests`.
- [x] Route the overwrite path that still calls raw `File.Delete(dest)` (in `FileOperationStatementHandler`,
      the pre-move/rename overwrite branch) and any recursive `Directory.Delete` through the validated
      authorizer, so no mutation boundary bypasses the link-race re-check.
- [x] Add link/junction substitution tests at the remaining boundaries (directory move/delete and
      overwrite-on-move), and assert the documented best-effort behavior on platforms where the OS
      returns no handle final path (`Resolve` → `null`).
- [x] Document the remote-filesystem (`IRemoteFileSystem`: SFTP/FTP/S3/Azure) mutation stance —
      handle-based re-check is local-only; state explicitly that remote delete/move/rename rely on the
      provider and are outside the OS-handle guarantee.

### Extend connect-time egress controls beyond REST — audit remaining clients
Already shipped: `PolicyBoundHttp.CreateHandler/CreateClient` enforces no-proxy, no-auto-redirect, and
connect-time DNS re-pin (`ConnectorPolicyAuthorizer.EnforceResolvedAddress`), and is wired into the
SharePoint, Report Portal, Orchestrator, OpenLineage, TUI `PortalClient`, remote policy runtime, secret
admin, and browser PDF export paths.
- [x] Audit every remaining production `new HttpClient` / `HttpClientHandler` (discovery, health/probe,
      vault, OIDC metadata/JWKS) and migrate any raw client to `PolicyBoundHttp`; add a guard test that
      fails when a production HTTP client is constructed outside the policy-bound factory.
- [x] Add redirect re-authorization coverage: assert a 3xx to a policy-denied host is re-validated and
      refused (not silently followed) across at least the Portal, Orchestrator, and SharePoint clients.

### Portal administrator UI for policy lifecycle — SHIPPED (verify only)
`policy-authority-admin.js` + `PolicyAuthorityController` already cover validation, staged publication,
activation, rollback, version history, machine enrollment inventory, machine revocation, and
signing-key status — the roadmap bullet verbatim.
- [x] Confirm Portal WebApplicationFactory coverage (`Category=Portal`) exercises the activate/rollback
      and machine-revoke mutation paths and their authority gate + audit emission; add cases if missing.
      No new UI work expected — mark the roadmap bullet shipped if coverage holds.

### Canary policy rollout — new work (the real remaining Phase 1 bite)
Building on branch `feat/canary-policy-rollout`. `PolicyRolloutState.Canary` + `CanaryCohort`
(named group OR deterministic/stable/ramp-monotonic percentage) added; a canary is served only to its
cohort while the fleet keeps `Active`.
- [x] Data model + migration (dual provider: `.Data` SQLite + `.Migrations.Postgres`) for machine
      groups (`PolicyMachineEntity.CanaryGroup`) and a cohort/percentage selector on the published
      version (`PolicyVersions.CanaryGroup/CanaryPercentage`). Additive/rolling-expand safe; convergence
      green. (slice 1)
- [x] Retrieval honors cohort assignment: `PolicyDistributionController` serves the canary to a machine
      whose registered identity/group is in the cohort; everyone else stays on active. Membership decided
      from the machine's own registration, never caller-supplied. Halt re-issues the active doc with a
      fresh (later) issuance so cohort machines actually revert (client rejects older issuance). (slice 2)
- [x] Promote / halt controls with the same signing/rollback guarantees as fleet-wide activation;
      publish-canary + promote + halt admin API (Admin-gated, audited) and Portal UI (canary card, cohort
      column, promote/halt row actions). UI verified in the ui-sandbox. (slice 3)
- [x] Certification: slice 2 proves a cohort member and the fleet receive different documents and that
      halt reverts the cohort; slice 1 proves the fleet active is untouched while a canary runs.
      Standalone (unenrolled) nodes never contact the authority, so they are unaffected by construction
      (documented in the Administrators_Guide canary section).
- [x] Governance: **audit** covers cohort ops (PUBLISH/PROMOTE/HALT_CANARY_POLICY, slice 3) and the
      cohort is visible in the versions API/UI, so a canary cannot silently move machines onto a
      different policy. **Decision 2026-07-13: do NOT extend the versioned `SecurityEventType` contract**
      for administrative cohort ops (its members are denials/overrides/failures/enrollment/limits; Portal
      admin actions use `AuditService`) — don't re-propose a canary security event without a contract review.
- [x] Docs: Administrators_Guide canary rollout section added (publish → ramp/promote → halt, cohort
      types, halt-reissue behavior). ROADMAP Phase 1 bullets 1–3 reconciled as shipped.

---

## Enterprise Policy Operations Documentation

Brought in from `ROADMAP.md` → *Enterprise Policy Enforcement & Monitoring → Phase 3:
Certification & Operations → Deployment and recovery*. This is the next enterprise item after
Phase 1 policy hardening/canary rollout.

- [x] Document policy-authority deployment, signing-key custody/rotation, machine
      enrollment/revocation, service-identity permissions, staged rollout, emergency policy
      publication, and unenrollment governance.
- [x] Document cache and outbox backup/restore rules; restored machines must not duplicate machine
      identity or silently reuse credentials in another environment.
- [x] Define upgrade ordering and compatibility across bootstrap, envelope, policy, event, and
      collector schema versions.
- [x] Provide outage runbooks for policy authority, certificate expiry, invalid publication, SIEM
      outage, disk exhaustion, and fail-closed fleet recovery.
- [x] Add support-bundle diagnostics that expose versions, hashes, timestamps, and health without
      policy payload values, trust material, credentials, or sensitive event targets.

---

## Enterprise Operations Control Plane

Brought in from `ROADMAP.md` → *Enterprise Policy Enforcement & Monitoring → Phase 4:
Operations Control Plane → 4.1 Central fleet management*.

- [x] Expand fleet inventory beyond current health aggregation to include node/environment identity,
      installed version, schema version, enrollment and policy compliance, last policy refresh,
      signing/client-certificate expiry, configuration drift, storage provider, database provider,
      and upgrade readiness.
- [x] Add fleet search, filtering, grouping, and drill-down without granting the aggregator mutation
      authority over departmental environments.
- [x] Define explicit machine/node registration, retirement, duplicate identity, stale heartbeat, and
      revoked-node behavior.
- [x] Surface unsupported version combinations, missing required capabilities, unhealthy dependencies,
      and policy divergence as actionable findings.

### Upgrade orchestration and compatibility
- [x] Define and automate the supported rolling-upgrade sequence: readiness check, node drain, binary
      deployment, database migration ownership, compatibility window, health verification, traffic
      restoration, and rollback decision.
- [x] Publish machine-readable compatibility metadata for Portal, Orchestrator, engine, database
      schema, policy/envelope schema, snapshots, plugins/connectors, and collectors.
- [x] Prevent two nodes from racing to run incompatible migrations; expose migration leader,
      progress, failure, and recovery state.
- [x] Add fleet-wide preflight and postflight reports while leaving package deployment to established
      tools such as Intune, SCCM, Ansible, Kubernetes, or equivalent infrastructure.
- [x] Certify N-1 rolling compatibility where promised and fail clearly when a deployment exceeds the
      supported compatibility window.

### Standard observability export
- [x] Add a Prometheus-compatible Portal `/metrics` endpoint backed by the existing non-secret
      operational snapshot, with stable `environment`, `node`, and `component` labels.
- [x] Add Portal execution-job `ActivitySource` spans with standardized bounded dimensions and
      correlation/script-hash context for OpenTelemetry collectors or .NET listeners.
- [x] Export audit/security delivery health in Portal operational metrics: pending/failed counts,
      backlog bytes, oldest-pending age, dropped security events, and collector reachability state.
- [ ] Add first-class OpenTelemetry metrics and traces without imposing standalone overhead when
      exporters are disabled.
- [ ] Standardize dimensions for environment, node, job, report, dataset, connector, execution mode,
      status, policy version, and workload class while controlling high-cardinality labels.
- [ ] Export queue depth/age, active and throttled work, execution latency, rows, CPU, memory, GC,
      spill, connector latency, retries, failures, storage growth, database pool health, policy
      refresh, audit/security backlog, and delivery health.
- [ ] Correlate metrics and traces with structured logs, audit events, security events, job IDs,
      script hashes, and request correlation IDs.
- [ ] Keep observability exporters optional and ensure disabled exporters impose negligible
      standalone overhead.
