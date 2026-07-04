# ETL-SQL Product Roadmap

This document tracks work that remains outside the active sprint. When development begins, move the
next actionable phase into `TODO.md`. Shipped work belongs in `CHANGELOG.md`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`Docs/Strategy/Enterprise_Platform_Strategy.md`](Docs/Strategy/Enterprise_Platform_Strategy.md).

---

## v0.14.0 Enterprise Policy Enforcement & Monitoring

*Completes the enterprise controls whose protected enrollment and authoritative client runtime shipped
in v0.13.0. Standalone installations must remain unenrolled, unrestricted by organization policy, and
independent of network services.*

> **Status:** Phase 3 and the v0.14.0 release gates are active in [`TODO.md`](TODO.md). Phases 4–5 are
> deferred to v0.15.0 and remain here as the source plan. **Phase 6 (Operations Control Plane) remains
> candidate scope**; promote roadmap work into `TODO.md` only when its target release begins.

### Shipped foundation

- Machine-level enrollment, protected bootstrap, trust key, machine identity, and enroll/status/unenroll CLI (`4850f3c0`).
- Tenant-bound RSA-PSS signed policy retrieval, protected cache, rollback/expiry checks, final configuration precedence, diagnostics, dynamic reload, and fail-closed host refresh (`9e0dfbc`).
- The v0.14.0 work must consume `EnterprisePolicyRuntime.Current`; it must not introduce a second policy loader or configuration-precedence path.

### Phase 3: Policy Authority & Operation-Boundary Enforcement

#### 3.1 Policy authority

- [ ] Add an administrator-only policy API and Portal workflow to validate, version, publish, supersede, and retrieve organization policies by tenant/environment.
- [ ] Sign envelopes with an external certificate/key-store reference; never persist an exportable private signing key in the Portal database, configuration export, logs, backups, or support bundles.
- [ ] Authenticate enrolled machines, bind responses to tenant/environment, support client certificates, and reject unknown, revoked, or reassigned machine identities.
- [ ] Preserve immutable published versions and record author, reviewer/publisher, timestamp, policy hash, superseded version, and rollout state.
- [ ] Support staged rollout and emergency rollback by publishing a newer signed version; clients must continue rejecting envelopes with older issuance times.
- [ ] Add policy-authority availability, signing-key rotation, machine revocation, and publication audit coverage.

#### 3.2 Shared enforcement context

- [ ] Define one immutable execution-policy snapshot containing enrollment, policy version/hash, actor, execution mode, script hash, job/correlation ID, and effective governed values.
- [ ] Capture the snapshot when execution begins and pass it through CLI, TUI, Report Player, Portal, Orchestrator, child processes, parallel branches, and scheduled jobs.
- [ ] Define policy-refresh semantics for work already running: security revocation and expired policy fail promptly; ordinary limit changes apply no later than the next operation boundary.
- [ ] Return structured allow/deny decisions with policy key, sanitized requested value/target, effective constraint, and correlation data.

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

### v0.15.0 Phase 4: Central Security Events

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

### v0.15.0 Phase 5: Certification & Operations

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

### Phase 6: Operations Control Plane (Candidate Scope)

*These gaps are recorded under v0.14.0 so they are not lost, but they are not automatically release
blockers. Promote the highest-value work into `TODO.md` after Phases 3-5 expose the final operational
requirements; unfinished items may move to the next release without weakening the v0.14.0 security gates.*

#### 6.1 Central fleet management

- [ ] Expand fleet inventory beyond current health aggregation to include node/environment identity, installed version, schema version, enrollment and policy compliance, last policy refresh, signing/client-certificate expiry, configuration drift, storage provider, database provider, and upgrade readiness.
- [ ] Add fleet search, filtering, grouping, and drill-down without granting the aggregator mutation authority over departmental environments.
- [ ] Define explicit machine/node registration, retirement, duplicate identity, stale heartbeat, and revoked-node behavior.
- [ ] Surface unsupported version combinations, missing required capabilities, unhealthy dependencies, and policy divergence as actionable findings.

#### 6.2 Upgrade orchestration and compatibility

- [ ] Define and automate the supported rolling-upgrade sequence: readiness check, node drain, binary deployment, database migration ownership, compatibility window, health verification, traffic restoration, and rollback decision.
- [ ] Publish machine-readable compatibility metadata for Portal, Orchestrator, engine, database schema, policy/envelope schema, snapshots, plugins/connectors, and collectors.
- [ ] Prevent two nodes from racing to run incompatible migrations; expose migration leader, progress, failure, and recovery state.
- [ ] Add fleet-wide preflight and postflight reports while leaving package deployment to established tools such as Intune, SCCM, Ansible, Kubernetes, or equivalent infrastructure.
- [ ] Certify N-1 rolling compatibility where promised and fail clearly when a deployment exceeds the supported compatibility window.

#### 6.3 Standard observability export

- [ ] Add first-class OpenTelemetry metrics and traces, plus a Prometheus-compatible metrics endpoint where appropriate.
- [ ] Standardize dimensions for environment, node, job, report, dataset, connector, execution mode, status, policy version, and workload class while controlling high-cardinality labels.
- [ ] Export queue depth/age, active and throttled work, execution latency, rows, CPU, memory, GC, spill, connector latency, retries, failures, storage growth, database pool health, policy refresh, audit/security backlog, and delivery health.
- [ ] Correlate metrics and traces with structured logs, audit events, security events, job IDs, script hashes, and request correlation IDs.
- [ ] Keep observability exporters optional and ensure disabled exporters impose negligible standalone overhead.

#### 6.4 Historical capacity planning and sizing

- [ ] Retain or export capacity history sufficient to calculate peak and percentile CPU, memory, queue wait, execution duration, concurrency, spill frequency/bytes, connector latency, retry/failure rates, and dataset/snapshot growth.
- [ ] Add workload breakdowns by environment, node, job, report, dataset, connector, owner, and workload class without exposing sensitive row data.
- [ ] Produce sizing and trend reports that distinguish CPU, memory, storage, connector, database, and concurrency bottlenecks.
- [ ] Add saturation indicators and forecast thresholds so administrators can identify when to scale up, scale out, repartition workloads, or adjust schedules.
- [ ] Document benchmark-to-production sizing guidance and clearly state where measured workload history is required instead of synthetic estimates.

#### 6.5 Alerting and service objectives

- [ ] Define recommended SLIs/SLOs for availability, queue wait, execution success/latency, freshness, policy availability, audit/security delivery, database health, and recovery.
- [ ] Add configurable alerts for queue age/depth, sustained CPU or memory pressure, repeated spills, failed/retried jobs, stale snapshots/datasets, policy/signature failures, certificate expiry, outbox backlog, disk pressure, storage growth, database connectivity/pool exhaustion, and unhealthy fleet nodes.
- [ ] Support alert routing through standard observability systems rather than building a proprietary pager; include deduplication, severity, recovery notifications, and runbook links in emitted signals.
- [ ] Provide baseline thresholds but require administrators to tune them from measured workload and business criticality.

#### 6.6 HA topology and failure certification

- [ ] Publish supported standalone, departmental, and HA reference topologies with exact requirements for PostgreSQL, load balancing, shared artifact storage, certificates, DNS, service supervision, and network trust boundaries.
- [ ] Certify node loss, process crash, network partition, PostgreSQL failover, shared-storage outage, duplicate scheduler leadership, orphaned work, and recovery without duplicate or lost mutations.
- [ ] Document which components ETL-SQL coordinates and which remain responsibilities of PostgreSQL, load balancers, object/file storage, Kubernetes, Windows Services/systemd, or other infrastructure.
- [ ] Add topology-aware health and readiness checks so load balancers remove unsafe nodes without hiding whole-environment failures.

#### 6.7 Disaster recovery objectives

- [ ] Define supported RPO/RTO targets for each reference topology and identify the state, artifact, key, certificate, policy, outbox, and external dependency included in each target.
- [ ] Add scheduled restore drills that verify database consistency, artifact references, encrypted data/key availability, policy enrollment, service accounts, audit/security continuity, and orchestrator recovery.
- [ ] Prevent cloned restores from silently reusing machine identity or client credentials in another environment; require deliberate re-enrollment and credential rotation.
- [ ] Produce a machine-readable recovery report with achieved RPO/RTO, missing dependencies, data loss window, and operator actions.
- [ ] Document regional/site failure, split custody, backup retention, immutable/offline backup, and emergency access procedures.

#### Phase 6 prioritization gates

- [ ] Rank each workstream using measured administrative pain, customer deployment scale, security impact, and dependency on external infrastructure.
- [ ] Identify the minimum operations-control-plane subset required for v0.14.0 and explicitly defer the remainder rather than partially shipping an undocumented management promise.
- [ ] Preserve scoped read-only fleet aggregation by default; any future remote mutation or upgrade command requires a separate threat model, authorization design, approval workflow, and audit contract.

#### v0.14.0 release gates

- [ ] Complete threat-model and senior security review with all high-severity findings resolved.
- [ ] Pass full functional, performance, migration, recovery, v0.14.0-scoped enterprise certification, and standalone regression suites.
- [ ] Confirm documentation never claims OS-level containment against administrators or arbitrary alternate executables; mandate WDAC/AppLocker or equivalent controls where that boundary is required.

---

## v0.15.0 Adaptive Execution & Extended Large-Data Certification

The v0.14.0 billion-row program established a credible but deliberately narrow certification for
streaming scan, filter, projection, low-cardinality aggregation, and spill-backed `#temp` staging.
v0.15.0 should improve efficiency and concurrency without weakening bounded-memory behavior or
turning that result into an unsupported blanket billion-row claim.

### Phase 1: Spill allocation and GC efficiency

- [ ] Profile the Gate F `#temp` round trip by allocation type and call site; publish retained bytes,
  cumulative allocation, allocation rate, GC counts/pause, CPU time, and physical I/O before changing
  the implementation.
- [ ] Remove avoidable row, batch, Arrow-builder, and serialization object churn through pooled or
  reusable buffers with explicit ownership and deterministic release.
- [ ] Add allocation and GC regression budgets at 10M/50M plus the operator-run 1B certification;
  throughput improvements do not pass if peak memory containment or correctness regresses.

### Phase 2: Adaptive resource utilization

- [ ] Add a bounded resource controller that can adjust batch size, worker count, prefetch depth,
  spill concurrency, and operator grant requests from measured CPU, memory pressure, queue depth, and
  storage latency.
- [ ] Define stable hysteresis, minimum/maximum bounds, fairness across concurrent jobs, and explicit
  configuration overrides; adaptation must not oscillate or exceed governance policy.
- [ ] Preserve deterministic single-worker execution for debugging/certification and prove that
  adaptive mode scales down under pressure as well as up when capacity is idle.

### Phase 3: Performance regression quality

- [ ] Replace Gate F's catastrophe-only throughput floor with scenario-specific warning and failure
  bands derived from checked-in baselines, while retaining a portable absolute safety floor for
  slower supported hardware.
- [ ] Record runtime, hardware, configuration, commit, and variance across repeated samples; reject
  statistically meaningful regressions rather than relying on one unusually fast or slow run.
- [ ] Keep Gate F operator-run and outside smoke/release lanes, but require a current-commit run before
  publishing performance claims or closing a release candidate that changes certified paths.

### Phase 4: Extend operator-specific billion-row coverage

- [ ] Define separate admission and success criteria for external equi-join and sort at 1B, including
  skew, partition passes, extent counts, spill bytes, useful throughput, and required free disk.
- [ ] Add bounded 1B scenarios incrementally for high-cardinality grouping, eligible window shapes,
  holistic aggregates, and heterogeneous `MERGE`; a fail-fast memory contract is not equivalent to
  spill-to-completion certification.
- [ ] Publish the exact certified matrix and keep unsupported expressions, adversarial distributions,
  and row-engine fallbacks explicit. Do not introduce a blanket “all SQL at 1B” claim.

### Phase 5: Fallback coverage and execution transparency

- [ ] Emit plan/telemetry reasons whenever a query leaves a native columnar path, including the
  unsupported expression, type/coercion, collation, memory-admission, or semantic constraint.
- [ ] Rank fallback frequency and cost from representative workloads, then add native paths only where
  measurements justify them; retain the row engine as the correctness fallback.
- [ ] Add differential correctness and crossover benchmarks for every new native path so small and
  medium workloads do not regress for the large-tier headline.

### Phase 6: Concurrent, PostgreSQL, and failure soak certification

- [ ] Run sustained PostgreSQL-backed Portal/Orchestrator load at representative report/job/history
  counts and concurrent execution levels; measure pool saturation, query latency, scheduler fairness,
  lease behavior, and database growth rather than inferring HA performance from SQLite tests.
- [ ] Add multi-hour concurrent large-job soaks covering mixed scan, spill, join, and sort workloads
  under shared memory and disk budgets, including cancellation at each spill phase.
- [ ] Inject disk-full/low-space, slow disk, corrupt or incomplete extent, process crash, restart,
  orphan cleanup, and temp-root exhaustion; verify bounded recovery with no leaked grants, handles,
  extents, or silently duplicated/lost mutations.

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
- [ ] Adaptive execution demonstrates higher utilization when resources are idle and safe throttling
  under contention, with fairness and governance ceilings proven by automated tests.
- [ ] Every newly advertised 1B operator has an isolated, resumable certification scenario and an
  explicit non-goal list; operators that miss their criteria remain unadvertised.
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.

---

## Review Workflow & Data Stewardship

*Combines steward-facing governance with four-eyes review, certification, impact analysis, and
tag-driven policy enforcement. These capabilities are expected to be developed in the same sprint.*

Strategy: [`Docs/Strategy/Data_Stewardship_Strategy.md`](Docs/Strategy/Data_Stewardship_Strategy.md)

### Candidate phases

- [ ] **Phase 1: Stewardship Catalog**
  - Define governed tag metadata, validation, required scopes, aliases, and deprecation rules.
  - Add queries and documentation for missing owner, steward, contact, classification, and quality metadata.
- [ ] **Phase 2: Portal Stewardship Views**
  - Add searchable tag catalog, sensitive-data inventory, missing-owner views, stale-lineage views, and per-steward queues.
- [ ] **Phase 3: Review, Approval & Certification Workflow**
  - Add review and certification state for datasets, reports, jobs, and key lineage targets.
  - Require four-eyes approval for configured critical actions, including report publication and production job changes.
  - Enforce segregation of duties so users cannot approve their own changes.
  - Re-evaluate pending and approved requests when permissions, ownership, or user status changes.
  - Audit requests, comments, decisions, certification changes, and rejections while keeping export/import script-first.
- [ ] **Phase 4: Tag-Driven Policy Enforcement**
  - Extend Governance Core to block or warn based on lineage tags and classification metadata.
- [ ] **Phase 5: Impact Analysis**
  - Surface upstream and downstream impact for tables, columns, jobs, scripts, datasets, reports, subscriptions, owners, and stewards.
- [ ] **Phase 6: Quality & Freshness Stewardship**
  - Tie `EXPECT` and validation outcomes, freshness, SLAs, and quality trends to lineage targets.
- [ ] **Phase 7: External Catalog Sync**
  - Add stable external IDs, conflict rules, and reconciliation reports for external catalog integration.

---

## Debugger & Interactive Troubleshooting

*Adds a script debugger for ETL-SQL pipelines without compromising script-first execution or
zero-trust safety.*

### Candidate phases

- [ ] **Phase 1: Debug Protocol & Execution Hooks**
  - Define breakpoints, step over, step into, step out, pause, resume, cancellation, and variable or temp-table inspection contracts.
  - Ensure debug hooks do not change normal execution semantics.
- [ ] **Phase 2: CLI/TUI Debug Experience**
  - Add local debugging with breakpoints, current-statement context, variables, temp tables, and recent lineage entries.
- [ ] **Phase 3: VS Code Debug Adapter**
  - Add a VS Code debug adapter configuration that uses the same engine debug protocol.
- [ ] **Phase 4: Portal/Orchestrator Guardrails**
  - Define whether scheduled or Portal jobs can be debugged, who can attach, what is redacted, and how sessions are audited.
