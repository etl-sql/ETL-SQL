# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active priority override — Billion-row execution performance

**Decision (2026-06-30):** pause new Enterprise Phase 3–5 feature work after the already-committed
execution-policy and filesystem-authorizer foundations. Resume enterprise enforcement after the engine
has a credible, measured path to large analytical workloads. Security fixes and regressions remain
in scope; this pause applies to new enterprise capability.

The active implementation plan is under **Scale & operator-algorithm assessment** below. The target is
an ETL-SQL-owned columnar execution path—not embedding DuckDB or delegating the product's execution
model to another database.

---

## v0.14.0 — Enterprise Policy Enforcement & Monitoring

Completes the enterprise controls whose protected enrollment and authoritative client runtime shipped
in v0.13.0. Standalone installations must remain unenrolled, unrestricted by organization policy, and
independent of network services.

**Shipped foundation (v0.13.0, do not redo):** machine-level enrollment, protected bootstrap, trust
key, machine identity, enroll/status/unenroll CLI (`4850f3c0`); tenant-bound RSA-PSS signed policy
retrieval, protected cache, rollback/expiry checks, configuration precedence, diagnostics, dynamic
reload, fail-closed host refresh (`9e0dfbc`). All v0.14.0 work consumes `EnterprisePolicyRuntime.Current`
— do **not** introduce a second policy loader or configuration-precedence path.

> **Before starting any item:** verify it against the current code first — some foundations already
> exist (e.g. `SecurityService` path validation, the governance audit outbox, fail-closed audit
> interceptor) and parts of these phases may be partially implemented. Don't treat a roadmap line as
> net-new work until confirmed.
>
> **Scope note:** ROADMAP Phase 6 (Operations Control Plane) is *candidate scope* and stays in
> `ROADMAP.md` — promote the highest-value Phase 6 items here only after Phases 3–5 expose the final
> operational requirements.

### Phase 3: Policy Authority & Operation-Boundary Enforcement — PAUSED

> **Paused 2026-06-30:** preserve completed work and tests, but do not start another Phase 3 slice until
> the active billion-row performance program reaches its certification gates.

#### 3.1 Policy authority
- [ ] Add an administrator-only policy API and Portal workflow to validate, version, publish, supersede, and retrieve organization policies by tenant/environment.
- [ ] Sign envelopes with an external certificate/key-store reference; never persist an exportable private signing key in the Portal database, configuration export, logs, backups, or support bundles.
- [ ] Authenticate enrolled machines, bind responses to tenant/environment, support client certificates, and reject unknown, revoked, or reassigned machine identities.
- [ ] Preserve immutable published versions and record author, reviewer/publisher, timestamp, policy hash, superseded version, and rollout state.
- [ ] Support staged rollout and emergency rollback by publishing a newer signed version; clients must continue rejecting envelopes with older issuance times.
- [ ] Add policy-authority availability, signing-key rotation, machine revocation, and publication audit coverage.

#### 3.2 Shared enforcement context
- [x] Define one immutable execution-policy snapshot containing enrollment, policy version/hash, actor, execution mode, script hash, job/correlation ID, and effective governed values. *(Core `ExecutionPolicySnapshot` is captured by the evaluator at top-level execution and preserved across parallel forks.)*
- [ ] Capture the snapshot when execution begins and pass it through CLI, TUI, Report Player, Portal, Orchestrator, child processes, parallel branches, and scheduled jobs.
- [~] Define policy-refresh semantics for work already running: security revocation and expired policy fail promptly; ordinary limit changes apply no later than the next operation boundary. *(Snapshot freshness contract now distinguishes terminal unavailable/expired policy from ordinary version/hash refresh; operation authorizers still need to invoke it.)*
- [x] Return structured allow/deny decisions with policy key, sanitized requested value/target, effective constraint, and correlation data. *(`OperationPolicyDecision` is the shared operation-boundary result contract.)*

#### 3.3 Filesystem enforcement
- [ ] Route all script-driven reads, writes, deletes, moves, copies, archive extraction, directory enumeration, spill, export, snapshot, and artifact paths through one canonical path-authorizer.
- [ ] Enforce approved roots, read/write distinctions, maximum recursive depth, file-operation count, extension/type restrictions, and protected application/system paths.
- [ ] Resolve canonical targets before access and prevent bypass through `..`, relative paths, mixed separators/case, UNC/device paths, alternate data streams, symbolic links, junctions, hard links, and archive traversal.
- [ ] Re-check immediately before mutation to reduce check/use races; use handle-based validation where the platform supports it.
- [ ] Keep engine-owned spill/cache paths separate from user-selected destinations while applying explicit policy limits to both.

#### 3.4 Network and connector enforcement
- [ ] Enforce connector allowlists and destination host/port/scheme rules before DNS resolution and connection creation.
- [ ] Protect against DNS rebinding, redirects to denied destinations, proxy bypass, IPv4/IPv6 literal variants, loopback/link-local/private ranges, and credentials embedded in URLs.
- [ ] Apply the same authorization to REST, database, email, SFTP, object storage, remote policy/vault access, and connector-specific discovery/probe operations.
- [ ] Ensure aliases, plugins, saved connections, and connection-string forms cannot bypass connector classification or destination checks.

#### 3.5 Process, Docker, resource, and script-setting enforcement
- [ ] Gate external executables, arguments, working directories, environment inheritance, shell invocation, Docker images/registries, mounts, networks, privilege flags, and host access.
- [ ] Enforce parallelism, recursion, file-operation, email, string/result, memory/spill, execution-time, and other governed resource ceilings at runtime.
- [ ] Prevent `SET`, environment variables, command-line options, report parameters, saved sessions, plugins, and child processes from weakening locked or constrained values.
- [ ] Permit users to choose stricter limits; reject weaker values before execution and retain the enterprise value.
- [ ] Make every denial deterministic across in-process and spawned-process execution.

#### Phase 3 completion gates
- [ ] Every governed key maps to a named enforcement boundary or is removed from the policy schema as non-enforceable.
- [ ] A repository-wide security review finds no direct sensitive operation that bypasses the shared authorizer.
- [ ] Bypass suites cover Windows and Linux paths, links, DNS/redirect behavior, connector aliases, child processes, Docker mounts, script overrides, and concurrent policy refresh.
- [ ] Existing standalone tests prove no enterprise endpoint, certificate, cache, or organization restriction is required when unenrolled.

### Phase 4: Central Security Events

#### Event contract and emission
- [ ] Define a versioned structured security-event schema with stable event ID, severity/type, timestamp, actor/effective identity, host/node, tenant, script/job/correlation IDs, policy version/hash, sanitized target, decision, and reason.
- [ ] Emit events for override attempts, denied filesystem/network/connector/process/Docker operations, policy signature/expiry/rollback failures, stale or unavailable policy, machine enrollment changes, and repeated resource-limit violations.
- [ ] Separate security events from ordinary diagnostic logs and existing governance audit records while preserving correlation between all three.
- [ ] Redact credentials, query parameters, connection strings, environment values, filesystem data, and exception details before persistence or transport.

#### Durable delivery and monitoring
- [ ] Provide a durable local security-event outbox for every executable, with bounded storage, atomic append, retry, batching, deduplication, jittered backoff, and crash recovery.
- [ ] Deliver to an HTTPS/SIEM collector using machine identity; define acknowledgement and idempotency behavior.
- [ ] Add Windows Event Log and syslog/structured-file sinks for bootstrap failures that occur before HTTPS delivery is available.
- [ ] Support policy-controlled severity filters so enterprises can forward security warnings/denials without centrally shipping all diagnostic logs.
- [ ] Add optional fail-closed thresholds for terminal delivery failure, oldest-event age, pending count, and outbox bytes; standalone mode remains local-only by default.
- [ ] Expose queue health, last delivery, failures, drops, and collector reachability through diagnostics and fleet status.

#### Phase 4 completion gates
- [ ] Fault-injection tests cover collector outage, duplicate delivery, acknowledgement loss, corrupt outbox state, disk pressure, process crash, redaction, and recovery.
- [ ] A denial is blocked first and then reported; no enforcement decision depends on successful remote logging unless fail-closed monitoring is explicitly enabled.
- [ ] Documentation includes example mappings for common SIEM products without coupling the core event contract to one vendor.

### Phase 5: Certification & Operations

#### Certification lanes
- [ ] Add Windows and Linux enterprise certification lanes for enrollment, signed retrieval, cache/offline operation, dynamic refresh, operation enforcement, and event delivery.
- [ ] Certify Portal, Orchestrator, CLI, TUI, Report Player, Report Builder, Language Server, scheduled jobs, spawned runners, and parallel execution.
- [ ] Run malicious-input and bypass drills covering policy tampering, stale/expired policy, signing-key rotation, machine revocation, path/link races, DNS rebinding, connector aliases, Docker escape-oriented options, and log injection.
- [ ] Prove standalone regression behavior with no enrollment, no enterprise network calls, and unchanged local workflows.

#### Deployment and recovery
- [ ] Document policy-authority deployment, signing-key custody/rotation, machine enrollment/revocation, service-identity permissions, staged rollout, emergency policy publication, and unenrollment governance.
- [ ] Document cache and outbox backup/restore rules; restored machines must not duplicate machine identity or silently reuse credentials in another environment.
- [ ] Define upgrade ordering and compatibility across bootstrap, envelope, policy, event, and collector schema versions.
- [ ] Provide outage runbooks for policy authority, certificate expiry, invalid publication, SIEM outage, disk exhaustion, and fail-closed fleet recovery.
- [ ] Add support-bundle diagnostics that expose versions, hashes, timestamps, and health without policy payload values, trust material, credentials, or sensitive event targets.

### Administrator operational review — follow-on hardening (2026-07-01)

> Derived from an administrator walkthrough of a two-server (Portal + Orchestrator) enterprise
> deployment (~1000 users, 50 reports, 100 jobs incl. 20 outbound vendor SFTP). Verified against
> current code: the identity system variables, host-utilization capture, JobHistory retention, and
> SFTP host-key/atomic-upload behavior below **do not exist today**. Some items overlap ROADMAP
> Phase 6 (Operations Control Plane) candidate scope; promoted here because they are concrete gaps an
> administrator hits before Phase 6 lands.

#### Row-level security — expose caller identity to report/script SQL

> Full design: [`Docs/Design/RowLevelSecurity.md`](Docs/Design/RowLevelSecurity.md). Decisions locked:
> admins bypass RLS by default (configurable), group/role matching is case-insensitive, OIDC group
> claims are a required source, Phase 1 = variables + `HAS_GROUP`/`HAS_ROLE` + enforcement + admin
> impersonation. `Publisher` is the report-writer role, distinct from `Admin`.

- [x] **Phase 1 engine:** Read-only identity system variables (`@@CURRENT_USER`, `@@CURRENT_USER_ID`, `@@REAL_USER`, `@@IS_ADMIN`) in both `SystemVariableProvider` and `ExpressionEvaluator`, plus `HAS_GROUP('name')` / `HAS_ROLE('name')` predicates with default-on admin bypass. *(commit `9c379b52`)*
- [x] **Enforce `@@` immutability explicitly.** `SET` / `DECLARE` targeting a `@@` name are now rejected with clear errors instead of relying on resolution-order side effects. *(OUTPUT-parameter path inherits the DECLARE guard; parser-level rejection still a possible hardening.)*
- [x] Identity is host-injected via the `ExecutionIdentity` channel (Core `IExecutionContext`), flows across `Evaluator.Fork`, and is populated by the Portal from the executing user's DB roles/groups (incl. OIDC groups) — never from parameters/environment/sessions. *(commit `8729677a`)*
- [x] Snapshot cache-leak closed: identity-sensitive reports (conservative static scan) no longer persist a shared `ReportSnapshot`; the executor still gets their per-job manifest. *(Subscriptions per-recipient and scheduled-refresh disable for sensitive reports still remain.)*
- [x] Admin bypass (default on, `Portal:Security:AdminBypassRowLevelSecurity`) keyed on the effective identity; fails closed (null identity → `HAS_GROUP` false, `@@CURRENT_USER` null).
- [x] Admin real-impersonation endpoint `POST /api/reports/{id}/execute-as/{targetUserId}`: effective = target (incl. target's bypass status), real = admin via `@@REAL_USER`, unconditionally never-cached, audited as `EXECUTE_REPORT_AS`. *(commit `66dc5715`)*
- [x] Administrators_Guide §4 Row-Level Security section (authoring, security properties, admin bypass/impersonation).
- [x] Scheduled/background refresh (trusted, non-interactive) of an identity-sensitive report is skipped — no shared snapshot to update, so no execution slot is burned.
- [x] End-to-end Portal integration test (`RowLevelSecurityPortalTests`): identity-sensitive report persists no shared snapshot (plain report does); admin `execute-as` runs, audits the real actor + target, and shares no snapshot. Engine filtering covered by `RowLevelSecurityIdentityTests`.

**Phase 1 complete.** Phase 2 (below) is the remaining RLS work.

##### RLS Phase 2 (enterprise completion)
- [x] Table-valued `USER_GROUPS()` / `USER_ROLES()` for `WHERE col IN (SELECT Value FROM USER_GROUPS())` joins; both added to the identity-sensitivity scan so they are also never-cached. *(3 engine tests)*
- [x] Publisher **preview-as**: `execute-as` extended to folder editors (Manage), not just admins. Security-review question resolved (not an escalation — the previewer's own authority gates data access; the simulated identity only drives author-written RLS predicates; DB-layer isolation is out of scope). Never-cached, dual-identity audit. *(Portal test: editor allowed, execute-only viewer forbidden)*
- [x] **Per-recipient subscription delivery** for identity-sensitive reports: each recipient email is resolved to a portal user and the report runs under **their** identity (their filtered view). `ExecutionIdentity` now threads through `IScriptExecutor.ExecuteTextAsync` → `ExecutionSession` → evaluator (optional param; out-of-process `ProcessJobExecutor` fails closed by design). Unresolvable/external recipients fail with a clear ledger reason rather than an empty delivery. Recipient→user lookup materializes + decrypts in memory because `Email` is PII-encrypted (non-deterministic). *(2 delivery tests: resolvable recipient runs under their groups; unknown recipient fails clearly)*
- [x] CLI / Orchestrator run-as: non-interactive execution (`ScriptExecutorAdapter`/`ExecutionSession`) never sets `ExecutionIdentity`, so identity-sensitive scripts fail closed (no rows) — the correct safe default. An **explicit** operator-supplied run-as identity for scheduled jobs is an optional future enhancement, not a safety requirement.
**RLS Phase 1 & 2 complete** (impersonation/preview-as, OIDC groups, `USER_GROUPS()`/`USER_ROLES()`, per-recipient subscriptions, docs — all shipped above). Only an optional explicit operator run-as for scheduled jobs remains, and the fail-closed default already makes that safe.

#### Liveness alerting and job-failure notification
- [x] Administrators_Guide §9.1 documents the external heartbeat / dead-man's-switch (out-of-process `/healthz` probe), the per-job-immediate vs daily-digest job-failure patterns (with the "in-orchestrator digest can't report its own host down" caveat), and the workload-vs-host-saturation distinction for capacity.
- [x] Template failure-digest job shipped at `samples/admin_operations/daily_failure_digest.etlsql`: `SHOW JOB HISTORY INTO #t` → filter recent non-`SUCCESS` runs (`DATEADD`/status) → `STRING_AGG` body → `SEND EMAIL`. The mechanic is verified in-process against a seeded `IJobHistoryStore` and the full template is parse-checked (`JobFailureDigestTemplateTests`, 2 tests) — no Docker needed since `SHOW JOB HISTORY` reads the local store. Also fixed the `SEND EMAIL` help doc (`AT connectionName`, not `@`).

#### Backup scheduling and observability
- [x] Administrators_Guide §8 documents scheduling the existing `etl-sql admin backup` CLI externally (survives orchestrator downtime), alerting on its exit code, and a periodic `--validate` restore drill; plus the PostgreSQL-is-your-tooling's-job note.
- [ ] Backup-status-to-JobHistory row — deferred: backup is a CLI command, not a scheduled job, so this belongs with the prebuilt scheduled-backup template rather than the engine. Explicitly **not** adding an in-language `BACKUP` statement (would appear to cover PostgreSQL, which it must not).

#### Outbound SFTP hardening
- [x] SFTP server host-key verification: `HOST_KEY_FINGERPRINT` option pins the server key (SHA256 base64 or MD5 hex, optional algo prefix). `SftpConnector` now wires `HostKeyReceived` and rejects a mismatch (MITM protection); unpinned connections proceed but log a warning (backward compatible). Matching logic unit-tested (6 cases: SHA256 padding/prefix, MD5 case/separators, mismatch, empty).
- [x] Opt-in per-connection atomic upload: `ATOMIC_UPLOAD=true` uploads to a temp name then renames into place; defaults off so write-only vendors are unaffected. Documented in the SFTP connector help (partial-file risk when off; rename permission required when on).

#### Capacity visibility and retention
- [~] Capture host/server utilization, distinct from per-job process cost. **Done:** free disk on state/spill volumes (`NodeCapacitySnapshot`, `DriveInfo`) alongside host memory-load % and CPU %; **persisted time series** — `HostMetrics` table + `IHostMetricsStore` (append/get/prune) sampled every heartbeat, pruned on the maintenance cycle (`Orchestrator:HostMetricsRetentionDays`, default 14); **read surface** — `SHOW HOST METRICS [nodeId] [INTO #t]` returns the last 24h newest-first (mirrors `SHOW JOB HISTORY`), usable in capacity-report scripts. *(store test + end-to-end SHOW test)* **Remaining (planned, see design doc):** whole-host (not process) CPU probes.
- [x] Configurable JobHistory retention: `IJobHistoryStore.PruneHistoryAsync(maxAge)` deletes completed rows older than `Orchestrator:JobHistoryRetentionDays` (default 30; 0 disables), run on a `Scheduler:HistoryPruneIntervalMinutes` cycle (default 360) alongside session reaping. RUNNING rows are never pruned. *(store test)* Portal execution-ledger retention still remains.
- [x] Orphaned-RUNNING recovery (gap found while reviewing retention): a hard crash between job start and completion left a JobHistory row `RUNNING` forever — unprunable (retention skips RUNNING) and invisible to failure reporting. `ReconcileStaleRunningAsync(maxRuntime)` now marks RUNNING rows older than `Orchestrator:MaxJobRuntimeHours` (default 24) as `INTERRUPTED` on scheduler startup and each maintenance cycle; self-healing (a late completion overwrites it) and now caught by the failure digest. *(2 store tests)*
- [x] Daily roll-up summary retained far longer than raw rows, so pruning bounds table growth without losing capacity-planning trend. Two idempotent, transactional roll-ups (DELETE-affected-days + re-INSERT, grouped by `substr(timestamp,1,10)` so it is portable across SQLite/Postgres): `JobHistoryDaily` (per day/job: run count, failure count, total rows, max peak memory) and `HostMetricsDaily` (per day/node: avg+max memory-load %, avg+max CPU %, min free state/spill disk). Run **before** raw pruning on the maintenance cycle via `RollUpJobHistoryAsync`/`RollUpHostMetricsAsync`; summaries pruned on their own long horizon `Orchestrator:HistoryRollupRetentionDays` (default 400). Read via `GetJobHistoryDailyAsync`/`GetHostMetricsDailyAsync`. *(store test: aggregation + idempotency + retention)*

> **Remaining host-utilization + capacity work is planned in
> [`Docs/Design/HostUtilizationAndCapacityPlanning.md`](Docs/Design/HostUtilizationAndCapacityPlanning.md)**
> (HostMetrics time-series table, `SHOW HOST METRICS` read surface, daily roll-ups, whole-host CPU
> probes, and the remaining templates), with a concrete sequencing. Next-cycle starting point.

#### Governance default
- [x] `Portal:Audit:RequireRemoteDelivery` is now nullable; when unset it resolves to **on** for an enrolled deployment with a collector configured (`TransportEndpoint`), **off** for standalone/unenrolled or no-collector deployments. Explicit `true`/`false` always wins — upgrade-safe (a deployment without remote audit is never newly blocked). *(6 resolve unit cases; Administrators_Guide §4 updated)*

#### Prebuilt administrator template scripts
- [x] (a) Daily job-failure digest email — shipped at `samples/admin_operations/daily_failure_digest.etlsql`, verified in-process + parse-checked.
- [x] (c) Capacity/utilization report — shipped at `samples/admin_operations/capacity_report.etlsql`: aggregates `SHOW HOST METRICS INTO` (min free disk / peak memory·CPU per node) and `SHOW JOB HISTORY INTO` (runs / non-success) into an emailed summary. Verified in-process (exact body expression) + parse-checked.
- [ ] (b) backup + status; (d) operational-metrics email subscription — planned in [`Docs/Design/HostUtilizationAndCapacityPlanning.md`](Docs/Design/HostUtilizationAndCapacityPlanning.md). Each template ships with an in-process mechanic test + full-file parse check (`SEND EMAIL` uses `AT connectionName`; statuses are `SUCCESS`/`FAILURE`/`BLOCKED`/`QUARANTINED`/`RUNNING`/`INTERRUPTED`).

### v0.14.0 release gates
- [ ] Complete threat-model and senior security review with all high-severity findings resolved.
- [ ] Pass full functional, performance, migration, recovery, enterprise certification, and standalone regression suites.
- [ ] Confirm documentation never claims OS-level containment against administrators or arbitrary alternate executables; mandate WDAC/AppLocker or equivalent controls where that boundary is required.

---

## Scale & operator-algorithm assessment (large-tier behavior)

### Active plan — ETL-SQL-owned columnar execution path (revised 2026-06-30)

**Engineering conclusion:** compact columnar `#temp` storage is necessary but insufficient. If every
read immediately reconstructs boxed `Row`/`DataTable` values, memory improves while CPU and allocation
cost remain fundamentally row-at-a-time. The performance architecture must expose native column
buffers directly to common operators, with the existing row path retained as a compatibility fallback.

**Scope discipline:** do not attempt to reproduce all of DuckDB at once. Build columnar fast paths for
the high-volume relational core while preserving ETL-SQL orchestration, connectors, governance,
lineage, scripting, and heterogeneous-source behavior.

#### P0 — Make certification truthful and diagnostic

- [x] Replace the misleading `peakManagedMemoryMB` metric. It previously sampled
  `GC.GetTotalMemory(forceFullCollection: true)` only after a scenario, so it is neither peak managed
  memory nor process working set. *(Continuous 100 ms scenario sampler now gates peak process working
  set and reports private bytes and managed heap.)*
- [x] Sample and report peak process working set, private bytes, managed heap, allocation bytes, GC
  counts/pause time, CPU time/utilization, spill read/write bytes, spill extent count, rows/second, and
  partition-pass count while each scenario runs. *(Process/GC/CPU/throughput and spill-write metrics
  shipped; spill-read and extent counters come from the spill-store boundary; external join,
  aggregate, distinct, and window partition/repartition sweeps now increment a dedicated pass counter
  emitted by certification.)*
- [x] Enforce a fixed machine-independent memory ceiling. Remove row-proportional Huge defaults (the
  current formula permits 80,000 MB at 10M rows). For the final lane, process peak must remain below
  16 GB; configure the engine grant around 8–10 GB to leave runtime/GC headroom. *(Defaults are now
  fixed at Smoke=1 GB, Standard=4 GB, Stress=8 GB, Huge=16 GB; explicit override remains supported.)*
- [x] Split correctness, memory, and throughput gates. A scenario must not report “certified” merely
  because it returned the right checksum after spilling. *(Separate fields are emitted; optional
  `CERT_MIN_ROWS_PER_SECOND` activates the throughput gate.)*
- [x] Capture a checked-in baseline for 10M and 50M before changing storage. Use Release + server GC,
  record machine CPU/RAM/disk, and report per-scenario metrics rather than one multi-scenario process
  whose retained state contaminates later measurements. *(The isolated 10M core baseline is checked in
  at `certification-results/baseline-10m.json`; the targeted pre-optimization 50M temp-spill baseline is
  at `certification-results/temp-spill-baseline-50m-pre-optimization/cert-report.json`. Join, sort,
  storage, and operator gates also retain same-runner 10M/50M comparison artifacts. A comprehensive
  pre-storage 50M run was not captured before the implementation changed and cannot now be recreated;
  that historical gap is explicitly recorded rather than represented as unfinished runnable work.)*

#### P1 — Fix spill I/O amplification before adding new operators

The prior `InMemoryDataSource.WriteBatches()` path cloned and validated every row, serially awaited
each spill, and emitted one chunk per processed batch. At 1B rows with 10K batches this could approach
100,000 spill chunks, making filesystem metadata and reader/writer setup dominate execution.

- [x] Introduce large sequential spill extents (initial target 64–256 MB), appending multiple logical
  batches per extent. Bound open files and record extent count in certification. *(Bulk `#temp`
  writes now coalesce logical batches into estimated 128 MB extents; pressure and explicit flush paths
  rotate at the same bound; certification reports physical read bytes plus extent count. External
  operator partition/run writers are bounded by adaptive fan-out and capped merge fan-in: Gate E
  records bounded join/sort extent counts, while Gate F coalesced 40,000 logical 25K-row batches into
  1,000 physical extents. A completed `WriteBatches` invocation intentionally closes its final extent;
  separate append calls are separate durability/visibility boundaries and are not held open globally.)*
- [x] Add a bounded double-buffered pipeline so producing/encoding the next batch can overlap writing
  the current extent without violating the memory grant or cancellation semantics. *(The `#temp`
  path now overlaps validation/production of one batch with encoding/writing of one batch, propagates
  writer failures, observes execution cancellation, and deletes an incomplete extent on failure.
  Producer and pending-writer slots now hold independent process-wide memory-grant leases; a rejected
  producer reservation waits for the writer to release its slot before retrying, while a batch that
  cannot coexist with retained table memory is handed directly to spill rather than retained. Focused
  tests prove overlap, grant-pressure backpressure, cancellation/failure cleanup, and zero leaked
  reservations.)*
- [~] Avoid `Row.Clone()` plus per-value Arrow rebuilding when input is already a native column batch.
  *(Compatible identity-projection `SELECT * INTO` now transfers retained native batches directly into
  a columnar sink with no row materialization or buffer rebuild. Supported filters compact selected
  ordinals directly into independently owned typed buffers; simple reordered/aliased identifier
  projections also compact and rename the native schema. UTF-8 selection copies offsets/data bytes
  directly without decoding managed strings. Expression projections and row-backed destinations still
  use the compatibility path.)*
- [x] Measure compression as an explicit disk-vs-CPU tradeoff; do not enable it by assumption.
  *(A reproducible 100K-row Release assessment now emits physical bytes, wall time, CPU time, and
  read-back checksums for compressed/uncompressed Arrow spill. On the 2026-06-30 workstation,
  repetitive data fell to 11.85% of original bytes with no measured time penalty; high-entropy data
  fell to 43.72% but cost 45.79% more write wall time and 7.28% more read time. Compression remains
  configurable because the crossover is workload and storage dependent.)*
- [x] Wire pressure-based spill into every host composition root, not only Orchestrator. Keep the row
  threshold as a backstop, but make bytes the authoritative trigger. *(App/CLI, TUI, Language Server,
  Report Player, and Portal composition roots register the shared buffer manager; `#temp` sources
  register as spillable and now reserve conservatively estimated resident bytes with the process-wide
  memory-grant arbiter. A rejected byte reservation spills even below the row threshold, while
  truncate, pressure spill, restore, flush, and disposal rebase or release the reservation.)*

#### P2 — Add a native column-batch contract and append-only `#temp` store

- [x] Add an internal `ColumnBatch` model with typed buffers, null bitmaps, row count, and schema.
  Storage uses arrays/`Memory<T>`/pooled buffers; `Span<T>` is only a synchronous loop view.
  *(Core now has immutable ordered schemas, unmanaged typed buffers, bit-packed null maps, explicit
  pooled ownership/disposal, physical-type validation, and allocated-capacity accounting.)*
- [~] Add a dual-path source contract such as `IColumnarDataSource.ReadColumnBatches()`. Preserve
  `IDataSource.ReadBatches()` as the compatibility adapter for connectors and features that still need
  rows. Do not force column batches through `Row` between column-capable operators. *(The separate
  cancellation-aware columnar read contract now exists without changing `IDataSource`; an explicit
  row-boundary adapter maps declared logical types to native widths and restores existing engine
  coercion/null semantics on fallback. The append store implements native source/sink contracts, and
  simple read-only SELECTs now route compatible filtering/projection through native batches while
  unsupported expressions replay through the row evaluator. Broader operator routing remains.)*
- [~] Implement append-only segmented `#temp` storage first: bounded mutable head, immutable native
  segments, large spill extents, deterministic disposal, and a row adapter only at fallback boundaries.
  *(A standalone append-only column data source now freezes a bounded compatibility head into pooled
  immutable segments, accepts native batches by ownership transfer, serves retained repeatable native
  reads, materializes rows only for `IDataSource` fallback, supports memory-releasing `TRUNCATE`,
  normalizes row writes to declared types, and enforces `NOT NULL` on row/native input. Integrating it
  also supports nested transaction snapshots through retained immutable segments, restoring row counts
  and constraint keys on rollback without copying payload buffers. An explicit default-off engine mode
  now creates eligible declared `#temp` schemas on the append store, while identity/default/check/FK or
  unsupported physical schemas retain the row store. Persistent sessions also remain on the row store
  until native segment manifests can be saved/restored. Default routing, mutation eligibility, native
  session persistence, and segment-native spill remain; runtime `CREATE INDEX` and `INSERT OR REPLACE`
  now fail explicitly in opt-in mode instead of silently dropping their semantics.)*
- [~] Store integral types as native integral widths, floating-point as `double`, `decimal` only for SQL
  decimal, dates/times as native fixed-width values, booleans as bit/byte buffers, and strings using
  offset/data or dictionary encoding selected from measured cardinality. *(The physical model now
  enforces unmanaged native fixed-width buffers and provides pooled UTF-8 offset/data strings with
  null preservation. The boundary mapper handles integral widths, double, SQL decimal, byte booleans,
  dates/times, GUIDs, and strings; bit-packed booleans and measured dictionary selection remain.)*
- [x] Use pooled/slab allocation and explicit ownership so large buffers do not create uncontrolled LOH
  churn. Account allocated capacity—not only logical payload—against the memory grant. *(Fixed-width,
  UTF-8, and null buffers are pooled; batches use retained references and stores deterministically
  release ownership. The mutable head reserves estimated row bytes, immutable segments reserve actual
  rented-array capacity, rejected native batches retain caller ownership, and segment reservations stay
  active until the final retained reader returns the buffers.)*
- [x] Replace PK/unique full-row caches with compact key/hash structures before declaring constrained
  columnar temp tables memory-bounded. *(Row-backed `#temp` unique indexes now keep value-aware compact
  key sets instead of `List<Row>` buckets, retain keys across spill, clear keys while preserving index
  definitions on truncate, and account key bytes incrementally. The native append store now enforces
  column-level PK/unique constraints directly from row or native buffers with persistent typed key sets,
  SQL null semantics, transactional staging, and a dedicated memory-grant lease. Table-level composite
  PK/unique constraints use canonical packed typed keys rather than retained rows or component-object
  arrays. Eligible constrained `#temp` schemas now route to the native store by default; end-to-end
  coverage proves composite unique enforcement stays on that route.)*

#### P3 — Columnar fast paths (“columnar islands”)

Operators use native batches when supported and fall back to the existing row engine for complex or
unsupported expressions. Scalar typed-buffer loops come first; SIMD is an optimization after profiling.

- [~] Scan and projection without materializing `Row` objects. *(Native batches now support zero-copy
  projections that retain the source ownership lease and expose selected buffers directly. Simple
  read-only SELECTs over `IColumnarDataSource` now scan/filter/project without rows and materialize only
  at the required `DataTable` result boundary. Compatible `SELECT * INTO` now transfers batch ownership
  directly between native sources and sinks, while supported predicates and reordered/aliased identifier
  projections compact native selections without rows. Expression projection and complex-plan routing remain.)*
- [~] Comparison, null, boolean, and simple arithmetic predicates using selection vectors. *(Pooled
  selection vectors now support composable typed fixed-width comparisons, SQL null exclusion,
  `IS NULL`/`IS NOT NULL`, boolean filtering, checked add/subtract/multiply and division predicates,
  cancellation, and ordinal validation without `Row` materialization. A conservative AST binder now
  recognizes fixed-width comparisons, reversed literal comparisons, arithmetic comparisons, and
  `IS NULL`/`IS NOT NULL`; `AND` composes selections and `OR` uses a pooled bitmap to deduplicate while
  preserving candidate order. UTF-8 columns now support all six comparisons while preserving the row
  engine's numeric/date coercion, SQL null behavior, and configured ordinal case sensitivity. Common
  non-coercing ASCII equality stays encoded and allocation-free; Unicode/coercing comparisons decode
  individual values without constructing rows. Unsupported expressions return to the row fallback.
  Broader complex-plan routing remains.)*
- [x] `COUNT`, `SUM`, `MIN`, `MAX`, and `AVG` over native buffers. *(Scalar typed-buffer kernels now
  implement SQL null exclusion, empty-input semantics, decimal-promoted accumulation, floating and
  decimal averages, optional selection vectors, cancellation, and fixed-width min/max without boxed
  row accumulation. Global aggregate SELECTs now bind these kernels across multiple native batches,
  including filtered input and safe replay into the row aggregate pipeline. Grouped SELECT plans bind
  the same aggregate set across batches and multiple numeric value columns. Global MIN/MAX also retain
  native routing for date/time and GUID buffers, with null and cross-batch coverage.)*
- [~] Low-cardinality `GROUP BY` with compact typed keys and memory-bounded aggregate state. *(A fused
  fixed-width native kernel now groups typed nullable keys and maintains row count, non-null count,
  checked sum, min, and max directly from buffers or selection vectors. Estimated per-group state is
  held under a result-lifetime memory-grant lease and fails explicitly when the grant is exhausted.
  The same typed state now accumulates across multiple native batches under one lifetime lease, and
  grouped AVG finalizes from decimal sum/non-null count with SQL empty semantics. The SELECT planner
  now binds one fixed-width numeric/date/time/GUID key plus COUNT/SUM/AVG/MIN/MAX over one fixed-width
  numeric value column, including null keys, supported native predicates, and first-batch row fallback.
  Key-only COUNT(*) plans use a separate leased count state, so temporal/GUID keys do not require a
  fabricated numeric value buffer. Aggregates over multiple numeric value columns use independently
  leased typed states merged by the shared key. HAVING over projected aggregate/key expressions is
  evaluated after native aggregation on the bounded group result; unprojected/complex HAVING falls back.
  String/composite keys and spill partitioning remain.)*
- [~] Hash partition routing directly from column buffers. *(Fixed-width nullable keys now route
  directly into one contiguous pooled ordinal buffer using a two-pass count/prefix/fill algorithm,
  with optional selection-vector input, deterministic null routing, cancellation cleanup, and
  O(rows + partitions) storage. String/composite hashing, planner integration, and adaptive fan-out
  remain.)*
- [~] Equi-join build/probe over typed key vectors and packed payload columns. *(A fixed-width inner
  equi-join kernel now builds typed key-to-ordinal state, probes native buffers or selection vectors,
  applies SQL null non-matching semantics, emits duplicate-preserving packed ordinal pairs, and holds
  bounded build/output reservations until result disposal. The same bounded kernel now supports left
  outer, semi, and anti semantics; unmatched rows use an explicit `-1` right ordinal, and randomized
  differential coverage proves duplicate/null behavior for all four variants. String/composite keys,
  direct payload projection, planner binding, and spill partitioning remain.)*
- [~] Sort-key extraction and run generation without boxing; retain the bounded external merge.
  *(Fixed-width keys now produce pooled typed sort runs from full batches or selection vectors using a
  cancellation-aware bottom-up merge, explicit null placement/direction, deterministic ties, and
  transient/result memory grants. Multi-key/string/collation extraction and planner integration with
  the existing bounded external merge remain.)*
- [~] Add adapters and differential tests proving columnar and row paths return identical results,
  null behavior, type coercion, collation behavior, lineage, and cancellation semantics. *(The row
  boundary adapter and deterministic differential coverage now compare fixed-width filtering,
  arithmetic, scalar aggregates, grouping, and sorting against row-reference results with nulls and
  decimal-to-integral coercion; kernel suites cover cancellation and ownership. End-to-end SELECT
  tests prove both native numeric routing and unsupported string-predicate replay without invoking a
  source row reader; global aggregate tests cover multi-batch decimal promotion and aggregate fallback.
  End-to-end grouped SELECT differentials now compare native and row planners across batches with null
  keys/values, filtering, and COUNT/SUM/AVG/MIN/MAX; native SELECT-INTO coverage also verifies lineage
  recording. String predicate coverage now proves case-sensitive/case-insensitive routing, Unicode
  fallback, numeric coercion, and null exclusion. Fixed-width join differentials now cover inner/left
  outer/semi/anti joins across duplicates and nulls. String/composite join differentials,
  derived-column lineage, and broader scripts remain.)*

#### P4 — Partition sizing and spill-read fast paths

- [x] Replace fixed `ExternalHashPartitions=32` with fan-out derived from sampled/known input bytes,
  key width, cardinality/skew evidence, and per-partition memory budget. Target one partitioning pass
  for normal distributions. *(A deterministic sizing model now accounts for payload bytes, row/key
  state overhead, target budget utilization, cardinality, skew, and configured bounds; it predicts
  partition passes and explicitly flags unsplittable hot keys. Partition-pass telemetry now supplies
  feedback. External join now samples up to 4,096 build rows or 16 MB for logical row/key width,
  distinctness, and hot-key evidence, then may increase fan-out above the configured baseline while
  replaying the entire sample. External distinct derives the same bounded evidence from its existing
  spill-triggering prefix and preserves full-row equality while adapting fan-out. Ordinary external
  GROUP BY samples and replays the same bounded prefix using evaluated grouping keys; grouping-set
  sizing uses the expanded `(set index, group key)` distribution and its row multiplier. External
  windows now sample evaluated PARTITION BY keys and replay all input rows. All runtime consumers of
  `ExternalHashPartitions` can increase the configured baseline. External aggregation and external
  join accept exact row/byte totals from already-materialized planner/build inputs and may safely reduce
  an oversized baseline. External windows do the same for their first signature pass, reverting to
  increase-only sampling after window columns change row width. Projected DISTINCT and other unknown
  streams deliberately never reduce from bounded samples because an unrepresentative prefix cannot
  prove that a smaller fan-out is safe.)*
- [x] Read spill extents as column batches. Do not reconstruct boxed rows merely to hash, filter,
  aggregate, or repartition them. *(New Arrow spill files now carry a versioned, ordinal logical-type
  schema; readers use it to preserve numeric/date-looking strings while metadata-free legacy files keep
  dynamic decoding. Arrow readers now implement an optional native batch contract with typed pooled
  buffers, null bitmaps, logical types, and exclusive row/batch consumption; Arrow writers accept typed
  batches directly with schema validation. Arrow UTF-8 offsets, bytes, and validity bits are copied
  directly into pooled native storage without per-value string decoding on reads.
  External join build hashing and compact-row packing now consume those batches without constructing
  build-side `Row` graphs; governed DISTINCT performs full-row hashing and compact retention the same
  way. Ungoverned DISTINCT also hashes batches directly and materializes only unique output rows.
  External join probes hash batches directly and materialize only matched or outer-preserved rows.
  Compatible numeric-key GROUP BY partitions reuse the native grouped planner directly over spill
  batches under both governed and ungoverned execution; unsupported schemas and explicit native memory
  pressure reopen through the existing row/repartition path. Compatible full-partition window aggregate
  and FIRST/LAST state scans consume batches, retaining row reads only for the required output replay.
  Recursive external joins, DISTINCT, and identifier-key aggregates hash native batches, compact
  per-partition selections, and write typed batches end-to-end. Grouping-set partition reads filter
  `__SET_IDX` in native batches and materialize only matching rows. Remaining row reads are semantic
  boundaries: evaluator-dependent expressions, holistic aggregate state, ordered sort/window processing,
  or rows that must be emitted; their broader kernels/state reduction are tracked in P5.)*
- [x] Detect skew and unsplittable hot keys explicitly; use bounded specialized handling or fail with a
  diagnostic under `SpillOrFail` rather than silently consuming unbounded RAM. *(External join,
  aggregate, and distinct recursion measure used/largest subpartitions, reject repartition attempts
  that do not reduce the hot partition, cap recursion depth, and invoke the memory-governor policy.
  `SpillOrFail` emits a skew/remediation diagnostic; `SpillOnly` explicitly opts into churn.)*

#### P5 — Mutation and broader semantics after the fast path is proven

- [ ] Add delete tombstones, update delta segments, and compaction only after append-only storage and
  columnar operator gates pass. Mutation complexity must not delay proof of the central architecture.
- [ ] Make `MERGE` bounded; it currently materializes source and target lists and is outside the initial
  billion-row claim.
- [ ] Reduce holistic aggregates to argument/order-key storage rather than full rows.
- [ ] Add streaming/specialized window paths where frame semantics permit; a single huge window
  partition remains outside the initial claim.
- [ ] Extend columnar kernels based on measured hotspots. Use `System.Numerics.Vector<T>` or
  `System.Runtime.Intrinsics` only where benchmarks show a material gain.

#### Required incremental gates

- [x] **Gate A — harness:** 10M baseline reports trustworthy peak process memory and throughput data.
  *(Each core scenario runs in a fresh Release/server-GC test host via `Test-ScaleBaseline.ps1`.)*
- [x] **Gate B — spill:** 50M append-only temp round-trip uses bounded large extents, stays below the
  configured ceiling, and materially improves rows/second versus the checked-in baseline. *(Owned
  SELECT-INTO batches now avoid a redundant row clone, row-backed spill crosses into Arrow through
  typed batches, and unindexed extents no longer retain batches solely for rollback bookkeeping.
  The isolated 10M run reached 228,786 rows/s with 425 MB peak working set and 43 extents versus the
  checked-in 226,035 rows/s/450 MB baseline. At 50M, the optimized path reached 249,890 rows/s with
  466 MB peak working set and 83 extents versus the same-runner pre-optimization result of 193,835
  rows/s and 800 MB. That is 28.9% higher throughput, 41.7% lower peak working set, 34.7% less managed
  allocation, and 38.7% less GC pause; the optimized run recorded 5.66 GB logical writes and 650 MB
  physical reads.)*
- [x] **Gate C — storage:** native columnar `#temp` uses materially less memory than `List<Row>` and
  does not decode to rows during a columnar scan/count/checksum. *(The isolated mixed-type storage gate
  streams bounded source batches and validates typed-buffer row-count/checksum scans. Native retained
  capacity measured 19.00% of estimated row heap at both 10M (100 segments, 470,844 rows/s) and 50M
  (500 segments, 494,258 rows/s). UPDATE, DELETE, CREATE INDEX, and INSERT OR REPLACE now preserve
  schema/constraints while downgrading once to the established mutable row store; WHAT_IF remains
  side-effect free. Transactional downgrade retains the original segmented store until commit and
  reconnects its transaction snapshot on rollback. Eligible non-persistent `#temp` tables now route
  to columnar storage by default, with `Engine:UseColumnarTempTables=false` as an explicit opt-out.
  The default-routing validation passed 4,266/4,267 full-suite tests; the sole shared-memory-budget
  aggregate failure passed in isolated rerun and did not exercise temp-table routing.)*
- [x] **Gate D — operators:** scan/filter/project and low-cardinality aggregate fast paths pass
  differential correctness and show a substantial throughput improvement at 10M/50M. *(The isolated
  Release/server-GC gate compares identical filter/projection/group checksums and requires at least
  1.5x speedup. Measured native versus row throughput: 63.30M versus 15.98M rows/s at 10M (3.961x),
  and 72.36M versus 17.66M rows/s at 50M (4.098x).)*
- [x] **Gate E — external operators:** equi-join and sort stay bounded with dynamic fan-out, bounded
  extent/file counts, and no unnecessary columnar→row→columnar round-trip. *(Join fan-out is adaptive,
  recursive hash paths use typed spill batches, and sort merge fan-in is capped at 64 readers. A
  deterministic 150-run sort proves two merge levels, three intermediate runs, and 153 total extents.
  Known-cardinality equi-joins now stream both inputs directly to the external partitioner instead of
  materializing the build side, partition planning respects the 256 MB per-operator grant, and recursive
  repartitioning is driven by the exact packed-build byte guard rather than a 5K-row cutoff. At 10M rows
  this reached 108,938 rows/s, 2.28 GB peak working set, 74 extents, and three partition passes versus
  the checked-in 39,399 rows/s/4.15 GB baseline. At 50M it sustained 107,152 rows/s below the 16 GB gate
  (10.0 GB peak), with 210 extents and three passes. Sort certification scales forced run size up to
  the production 100K cap while retaining multiple runs: 10M used 176 extents/two passes at 76,258
  rows/s and 1.07 GB peak; 50M used 625 extents/two passes at 73,070 rows/s and 1.10 GB peak.)*
- [x] **Gate F — initial 1B claim:** append-only scan, filter, projection, low-cardinality aggregate,
  and `#temp` round-trip complete below 16 GB process peak with documented CPU, disk, elapsed time,
  spill volume, and hardware. This does **not** initially certify arbitrary `MERGE`, holistic
  aggregates, single-partition windows, billion-distinct-key aggregation, or adversarial skew.
  *(A resumable `Test-GateF.ps1` runner now isolates the native scan/filter/projection/100-group
  aggregate and spill-backed `#temp` round-trip, writes durable child logs plus atomic `status.json`,
  enforces a 16 GB peak/50K rows-per-second floor, records the commit, and refuses to start the spill
  scenario without 25 GB free on the temp drive. Its 1B test is explicitly skipped outside that
  operator-run certification script; it is not part of smoke, ordinary test, or release lanes.
  The optimized user-operated run completed at commit `c039a462`: native
  scan/filter/projection/100-group aggregation processed 1B rows in 20.80 seconds at 48.1M rows/s
  with 145.8 MB peak working set and no row fallback; the spill-backed `#temp` round-trip completed
  in 942.2 seconds at 1.06M rows/s with 543.7 MB peak working set, 26.5 GB written, 4.2 GB read,
  1,000 extents, and no partition pass. The complete gate took 1,007 seconds (16m47s). Correctness,
  memory, and throughput gates all passed. Subsequent performance changes require a new Gate F run
  before replacing these published measurements.)*

#### Non-goals and guardrails

- Do not add DuckDB or another embedded database as the hidden execution engine.
- Do not promise blanket “DuckDB performance”; publish operator-specific throughput and limits.
- Do not remove the row engine. It remains the semantic fallback during incremental migration.
- Do not optimize solely for the 1B headline at the expense of normal small/medium workloads; every
  fast path needs crossover thresholds and regression benchmarks.
- Do not claim the current scale report proves peak memory containment until Gate A lands.

### Target: certify 1,000,000,000 rows below 16 GB process peak

**Status: the narrow Gate F workload is certified; this is not a blanket billion-row claim.** The
initial claim is deliberately limited to the operators listed below and the measured configuration
recorded above.

The first certified workload covers append-only scan, filter, projection, low-cardinality aggregation,
and `#temp` round-trip. Join and sort have separate Gate E requirements and may join the published 1B
matrix only after their measured throughput, spill passes, and skew behavior are acceptable. Arbitrary
`MERGE`, holistic aggregates, single-partition windows, billion-distinct-key grouping, and adversarial
skew remain explicitly outside the initial claim.

Success requires both bounded memory and useful throughput. “Finished eventually after spilling” is a
correctness result, not a performance certification. Every published result must include hardware,
Release/GC configuration, peak process memory, elapsed time, rows/second, CPU utilization, spill bytes,
extent count, partition passes, and required free disk.

**Other scale dimensions (the user's matrix):**
- [ ] **Script size** (10k lines × 10) — verify lexer/parser is O(n) and AST memory is bounded for very large scripts; `RUN SCRIPT` parse-caching already helps repeated targets.
- [ ] **Object count per script** (10/50/100 CONNECTION/VISUAL/@param/#temp) — agree with capping via policy limits (ties into v0.14.0 Phase 3.5 resource ceilings); 100 live connections in one script is an anti-pattern → guidance + a configurable limit rather than an algorithm change.
- [ ] **Server/orchestrator scale** (20/100/1000 reports/jobs) — the EF findings above (N+1, `AsNoTracking`, client-eval) are the relevant levers; verify scheduler/job-history queries paginate and index well at 1000 jobs.

### Measured: Stress tier (5M, 100x) — 2026-06-29

All 13 scenarios **passed** at 5M (correctness holds at scale; validates the ORDER BY sort-key refactor). Observations:
- **Streaming ops scale excellently** — `StreamingSelect`, `CsvIngest`, `ParquetRoundTrip` ran in 1–4 s with **0 spill**. Confirms FILTER/projection are not data-scale-bound.
- **Join slowest single op** (130 s / 5M, grace hash + repartition) — works; the perf cost of spilling joins.
- **Sort** handled 5M (66 s, 876 MB) with the cert's forced 5k chunk size = ~1000 spill readers open at merge. This historical risk is now mitigated by a 64-reader multi-pass fan-in cap; large-tier remeasurement remains.

### 50M (Huge tier) — could not complete in this environment
Wired and attempted twice; both runs were killed at the identical point with **no OOM/exception** — the agent's background runner kills tasks during the multi-minute **silent** 50M row-generation stretches (no stdout). **Not an engine fault.** Partial observation: the engine **generated 50M rows and switched to `ExternalAggregateEngine` without OOM**. To get full 50M metrics + `baseline-huge.json`, run on a capable host in the **foreground**: `pwsh ./scripts/Test-ScaleCertification.ps1 -Tier Huge`.
