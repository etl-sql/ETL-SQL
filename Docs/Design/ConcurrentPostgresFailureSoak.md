# Concurrent PostgreSQL and Failure Soak Certification (v0.15.0 Phase 6) — Design

**Status:** Implementation in progress; Slice A topology harness is implemented.
**TODO items covered:** v0.15.0 Phase 6 (PostgreSQL-backed Portal/Orchestrator sustained load,
multi-hour concurrent large-job soaks, disk/corruption/crash/cancellation recovery).
**Completion gate:** PostgreSQL sustained-load and concurrent failure-soak suites pass with
documented capacity and recovery limits, while normal small/medium regression lanes remain green.

---

## 1. Goal

Earlier phases improve single-process efficiency and broaden operator coverage. Phase 6 proves the
system behaves under the operational conditions an enterprise deployment actually sees: shared
PostgreSQL state, multiple Portal/Orchestrator nodes, concurrent large jobs, cancellation, slow or
full disks, corrupt spill extents, process crashes, restart cleanup, and sticky report sessions.

This is a certification plan, not a new feature bucket. It should mostly compose shipped HA,
governance, lease, heartbeat, spill, and scheduler behavior into repeatable soak lanes.

### Non-goals

- **No SQLite inference for HA claims.** SQLite/local storage remains the single-node default, but
  Phase 6 HA evidence must use PostgreSQL and shared artifact roots.
- **No unauthenticated Orchestrator exposure.** API routes remain protected by `X-Orchestrator-Key`.
- **No destructive production tests.** Fault injection uses isolated temp roots and disposable
  databases.
- **No Docker-only visual inspection loop.** Browser-side UI changes still use the UI sandbox first.

---

## 2. Test Topologies

| Topology | Purpose |
| :--- | :--- |
| Single-node SQLite/local | Regression baseline; proves defaults remain simple |
| Single-node PostgreSQL/local artifacts | Separates database-provider behavior from HA fan-out |
| Two Portal nodes + one Orchestrator + PostgreSQL + shared artifact root | Minimum HA topology |
| Two Portal nodes + two Orchestrator workers + PostgreSQL | Lease/fencing and scheduler fairness |
| Process-spawned worker pool | Isolation/cancellation/crash recovery |

HA runs require:

- `Portal:Database:Provider=Postgres`
- `Orchestrator:Database:Provider=Postgres`
- shared Portal artifact root (`Smb`/UNC or equivalent test share)
- shared Data Protection key ring
- identical JWT/orchestrator/dataset keys across Portal nodes
- sticky routing via `ETLSQL_PORTAL_AFFINITY` or configured affinity cookie

---

## 3. Sustained PostgreSQL Load

Workload dimensions:

| Dimension | Examples |
| :--- | :--- |
| Reports | catalog browse, parameter changes, dataset refresh, saved views |
| Jobs | scheduled ETL scripts, report refresh jobs, failed/retried jobs |
| History | high job-history count, host metrics, daily rollups, pruning |
| Users | local/OIDC users, group permissions, concurrent report sessions |
| Data volume | small reports plus large spill-backed jobs in the same window |

Metrics:

- PostgreSQL connection pool saturation and wait time.
- Query latency p50/p95/p99 for catalog/history/session operations.
- Scheduler queue depth and job start delay.
- Lease acquisition latency and fencing conflicts.
- Node heartbeat freshness and failover time.
- Portal session affinity misses.
- Database size growth and retention/pruning effectiveness.

The run should produce a capacity report under `certification-results/` with exact configuration,
row/report/job counts, duration, and observed limits.

---

## 4. Concurrent Large-Job Soak

The large-job soak mixes operators so it exercises shared memory, spill disk, and scheduler
fairness:

| Workload | Purpose |
| :--- | :--- |
| Streaming scan/filter/projection | Baseline throughput under concurrency |
| Temp-table spill round trip | Spill write/read and cleanup |
| External sort | Disk-heavy blocking operator |
| External join | partition/repartition behavior |
| External aggregate | high group-cardinality pressure |

Required observations:

- No leaked `MemoryGrantArbiter` reservations after completion/failure/cancellation.
- No leaked spill extents, file handles, or temp roots after cleanup.
- Fairness: no admitted job starves while another monopolizes shared resources.
- Adaptive execution, when enabled for its own scenario, scales down under contention.
- Cancellation at each spill phase leaves the session recoverable.

Default soak duration should be long enough to cross scheduler maintenance intervals and retention
logic in miniature. Manual release certification can run longer than CI; CI may run a reduced
smoke topology.

---

## 5. Fault Injection Matrix

| Fault | Injection point | Expected result |
| :--- | :--- | :--- |
| Disk full / low space | Spill root before or during extent write | Clear failure, no leaked grants/handles, partial extent cleanup |
| Slow disk | Spill writer/reader wrapper delays | Throughput drops, no timeout unless configured, adaptive storage pressure visible |
| Corrupt extent | Modify/delete spill chunk before read | Sanitized execution failure, no silent row loss |
| Incomplete extent | Crash during write | Restart cleanup removes orphaned temp root or persistent session reports unrecoverable spill |
| Process crash | Worker killed mid-job | Lease expires/fences, job marked failed or recoverable, no duplicate mutation |
| Portal node loss | Kill node with active sessions | Sticky sessions fail over only where supported; node-local interactive sessions documented |
| Orchestrator leader loss | Kill leader during schedule window | New leader acquires lease; job not double-fired |
| PostgreSQL outage | Stop DB briefly | Health probes fail, jobs back off, durable state remains consistent |
| Cancellation | Cancel during scan, spill write, spill read, merge/repartition | Prompt cancellation and cleanup at each phase |

Fault injection must be deterministic and bounded. Tests should verify cleanup invariants after
each fault, not just that an exception was thrown.

---

## 6. Safety and Governance Requirements

- Mutation tests use generated disposable data and `WHAT_IF` or transaction rollback unless the
  test specifically proves committed mutation recovery.
- Raw secrets, connection strings, API keys, `ENC:` values, and `SECRET:` references are redacted
  from logs and reports.
- All filesystem paths are inside isolated test roots and resolved through engine boundaries.
- Failure tests must not delete arbitrary temp roots; cleanup targets are exact run directories.
- Orchestrator routes remain authenticated in every topology.

---

## 7. Delivery Plan

1. **Slice A — topology harness.** Add scripts/config templates to bring up disposable
   PostgreSQL-backed Portal/Orchestrator test topologies and write run metadata. *(Implemented:
   `scripts/New-Phase6Topology.ps1` validates the existing HA Docker Compose topology, generates
   isolated local env/data-root configuration with disposable credentials, emits non-secret
   `topology-metadata.json`, and optionally starts Docker only when `-Start` is passed.
   `scripts/Test-Phase6Topology.ps1` covers template validation, generated configuration, metadata,
   and secret omission.)*
2. **Slice B — sustained load.** Implement report/job/history workload drivers and capacity report
   output. *(In progress: `capacity-results/workloads/phase6-postgres-ha-sustained.workload.json`
   defines the PostgreSQL-backed HA Portal/Orchestrator sustained-load profile, and
   `scripts/New-Phase6CapacityWorkload.ps1` materializes a local runnable copy from a generated
   topology run without committing generated API keys. `scripts/Test-Phase6CapacityWorkload.ps1`
   validates materialization and the existing service-capacity harness schema. Real measured
   capacity reports and PostgreSQL-specific metrics remain open.)*
3. **Slice C — concurrent large-job soak.** Add mixed workload runner, resource assertions, and
   cleanup checks.
4. **Slice D — fault injection.** Add disk, corruption, crash, DB outage, node loss, and
   cancellation tests with deterministic cleanup verification.
5. **Slice E — publication.** Update admin/capacity docs with measured limits and known boundaries.

---

## 8. Test Plan

| Layer | Proves |
| :--- | :--- |
| Unit tests | Lease/fencing helpers, cleanup scanners, fault wrappers |
| Integration smoke | Single-node PostgreSQL topology starts, runs, and cleans up |
| HA integration | Multi-node heartbeat, leader election, sticky affinity, shared artifact access |
| Soak manual lane | Multi-hour concurrent workload with resource/fairness assertions |
| Failure lane | Each injected fault produces expected error and cleanup |
| Release dry run | Reduced topology can run from `Test-PreRelease` without blocking normal lanes |

---

## 9. Completion Criteria

- PostgreSQL sustained-load report exists with documented capacity and bottlenecks.
- Concurrent large-job soak passes without leaked grants, handles, extents, or duplicate work.
- Fault injection matrix has pass/fail artifacts and clear unsupported recovery cases.
- Admin and operations docs reflect measured HA limits, including node-local interactive sessions
  and required sticky routing.
