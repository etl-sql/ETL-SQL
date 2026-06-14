# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Practical High Availability

> Status: **active (v0.12.0).**
> Goal: run multiple Portal and Orchestrator nodes behind a load balancer against shared PostgreSQL
> state and shared artifact storage, with safe coordination (leases, fencing, leader election) and a
> certified rolling-deployment story. This is the first roadmap phase of the v0.12.0 cycle; subsequent
> phases (Governance Core, Enterprise Identity, Departmental Isolation) stay in `ROADMAP.md` and move
> here as each begins.
>
> Priority convention: **P1** the supported path that must exist and be correct before the HA claim;
> **P2** the verification, chaos, and rolling-deployment certification around it.
>
> Verified-against-code baseline (2026-06-14):
> - Portal database is **hardcoded to SQLite** (`UseSqlite` in `src/ETL-SQL.ReportPortal/Program.cs`
>   and `PortalDbContextFactory.cs`); the Orchestrator uses a **separate hand-written SQLite store**
>   (`SQLiteJobHistoryStore`). No provider abstraction, no `UseNpgsql`, no `migrate-database` CLI
>   (Architectural Gaps #1, #2).
> - **No artifact-storage abstraction** — scripts, snapshots, cached datasets, and keys use direct
>   filesystem paths (Gap #3). No `IStorageProvider`/`IArtifactStorage`.
> - Coordination groundwork that exists and should be built on, not rebuilt: the Orchestrator durable
>   per-job lease + `StartLeaseHeartbeat` (`SchedulerService`), the single-active-Portal instance lock
>   (`ExecutionJobService`), and the cross-connection coordination tests from v0.11.0 P2.2. **Missing:**
>   fencing tokens (Gap #5), database-backed *node* heartbeats, and leader election.
> - `/health` exists on both the Portal (`AddHealthChecks` + `MapHealthChecks`) and the Orchestrator
>   service; the lightweight LB-oriented `/healthz` (DB/storage/lease connectivity) does not.
> - v0.11.0 explicitly **deferred to this phase**: true OS-process (not in-process proxy) coordination
>   tests, and the disk-full / network-partition / clock-skew chaos scenarios. Per-user execution
>   fairness shipped in v0.11.0; per-*group* limits and cross-workload queue weighting were the
>   documented residual and land here.

### Phase 1 — PostgreSQL State Provider

- [ ] **P1.1 Extract database-provider interfaces for Portal + Orchestrator state.**
  Decouple from the hardcoded `UseSqlite`; allow SQLite (default) or PostgreSQL by configuration.
  Resolve the two-store split (Gap #1): the Orchestrator's hand-written SQLite store needs a provider
  strategy too, or to move onto the EF model.
- [ ] **P1.2 Create EF Core migrations for PostgreSQL.**
  Provider-agnostic model with Npgsql migrations alongside the SQLite ones; CI must build/migrate both.
- [ ] **P1.3 Implement `etl-sql admin migrate-database --from sqlite --to postgres --dry-run`.**
  Row-count verification and cutover checkpoints; fail closed on mismatch. Builds on the v0.11.0
  `etl-sql admin` command group.

### Phase 2 — Artifact Storage Abstraction

- [ ] **P1.4 Create a unified storage-provider interface** for scripts, snapshots, cached datasets, and keys.
- [ ] **P1.5 Implement Local and SMB/UNC shared storage providers** behind that interface.
- [ ] **P1.6 Enforce path-traversal guardrails and script immutability at the storage boundary.**
  Extend the existing `SecurityService` path/immutability checks to the storage abstraction so every
  provider inherits them (don't reimplement per provider).

### Phase 3 — Distributed Leases & Fencing

- [ ] **P1.7 Database-backed node heartbeats + execution/job leases.**
  Generalize the existing Orchestrator per-job lease/heartbeat and the single-instance lock into
  node-level heartbeats so multiple nodes can coordinate over shared state.
- [ ] **P1.8 Monotonically increasing fencing tokens** to reject stale writers during partitions (Gap #5).
- [ ] **P1.9 Database-backed leader election** for cluster singletons (e.g. running migrations once).

### Phase 4 — Stateless Node Operation

- [ ] **P1.10 Portal nodes read state and serve snapshots from PostgreSQL** (no node-local authoritative state).
- [ ] **P1.11 Load-balancer session affinity** for interactive IDE sessions.
- [ ] **P1.12 Lease-loss cancels local work:** a partitioned node that loses its database lease
  immediately cancels its running jobs.
- [ ] **P1.13 Lightweight `/healthz` endpoint** on Portal nodes checking database, storage, and lease
  connectivity for load-balancer probes (distinct from the existing richer `/health`).
- [ ] **P2.1 Node-capacity heartbeats + quarantine policy.**
  Heartbeat CPU/memory so overloaded nodes don't claim new leases; quarantine repeatedly-failing jobs
  to prevent cascade failures.

### Phase 5 — Rolling Deployment Certification

- [ ] **P2.2 Verify expand/migrate/contract migrations** for zero-downtime rolling upgrades (distinct
  from the in-place N→N+1 drill, which is single-node).
- [ ] **P2.3 True multi-OS-process tests** against PostgreSQL + shared storage (separate processes, not
  in-process proxies): simultaneous claims, cancellation, permission changes, restart recovery,
  conflicting administration, failover recovery, and task reclamation. (v0.11.0 deferred this here.)
- [ ] **P2.4 Deterministic chaos scenarios:** process termination between cross-resource steps,
  database/storage unavailability, network partition, disk exhaustion/pressure, and bounded clock skew.
  (v0.11.0 deferred disk-full / partition / clock-skew here.)
- [ ] **P2.5 Prove lease-loss + fencing under partition:** lease loss cancels local work and fencing
  tokens reject every stale writer after a partition heals.
- [ ] **P2.6 Certify mixed workloads under load:** interactive, scheduled, refresh, and subscription,
  with per-user **and per-group** quotas, queue fairness, administrative overrides, and
  node-capacity-aware claims. (Per-user fairness shipped in v0.11.0; per-group + weighting are the residual.)
