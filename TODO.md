# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Documentation Standards Backlog

- [x] Add `Docs/Standards/Engine_Coding_Standards.md` covering engine-source rules:
  `IExecutionContext.ResolvePath()` before file I/O, injected `ILogger`, no `Logger.Instance`, no
  `Console.WriteLine`, async I/O with `CancellationToken`, sanitized `ExecutionException` wrapping,
  and AST nodes as `record` types.
- [x] Add `Docs/Standards/Language_Syntax_Standards.md` covering keyword casing, when to add a
  keyword vs. a connector option, statement naming, AST/handler naming, option value conventions,
  and parser compatibility rules.
- [x] Add `Docs/Standards/Breaking_Change_Standards.md` covering `// COMPAT_BREAK: x.y`,
  `BREAKING_CHANGES.md`, required regression tests, high-risk parser/evaluator sites, and the
  one-minor-version warning period for parser syntax removals.
- [x] Add `Docs/Standards/Third_Party_Dependency_Standards.md` covering the FOSS-only policy,
  NuGet evaluation checklist, one-library-per-domain rule, license banner preservation, and required
  updates to `THIRD-PARTY-NOTICES.md` / `THIRD-PARTY-INVENTORY.md`.
- [x] Add `Docs/Standards/Source_Boundary_Standards.md` promoting the current Core / Engine /
  Connectors / Analysis / Reporting / ReportHosting / host-shell ownership guidance out of the
  strategy migration plan.
- [x] Add `Docs/Standards/Report_Runtime_Asset_Standards.md` covering the canonical shared runtime
  source under `src/ETL-SQL.ReportRuntime/Resources/Shared/`, generated host copies, required
  `node .\scripts\sync-assets.js` / `-Check`, and UI sandbox verification.
- [x] Add `Docs/Standards/Script_Composition_Standards.md` for repo-authored ETL-SQL scripts:
  `main.etlsql` orchestrators, `_environment.etlsql`, stage-script naming, contract comments,
  shared `#temp` table expectations, shallow `RUN SCRIPT` nesting, and generated `.rptsql`
  separation.

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

- [x] **P1.1 Extract database-provider interfaces for Portal + Orchestrator state.**
  *(done)* Both state stores are now provider-selectable (SQLite default), removing the hardcoded
  coupling. **Portal (EF):** new `ETL_SQL.Common.DatabaseProvider` enum + `PortalConfig.Database`
  (Provider/ConnectionString); `PortalDatabase.Configure()` replaces both hardcoded `UseSqlite` sites
  (`Program.cs` + the design-time factory, now `ETL_SQL_DB_PROVIDER`-aware for P1.2 migration
  generation); added `Npgsql.EntityFrameworkCore.PostgreSQL` (+ refreshed `THIRD-PARTY-INVENTORY.md` /
  `THIRD-PARTY-NOTICES.md`). **Orchestrator (hand-written):** new `IOrchestratorStoreFactory`
  config-selects the provider and is the single construction seam — every production
  `new SQLiteJobHistoryStore(...)` (`SubscriptionsController` ×4, `SubscriptionDeliveryStatusService`,
  `SubscriptionScriptMaintenance`) now routes through it, and the DI singleton is provider-aware.
  Postgres **fails closed** in both stores until P1.2. Tests: `OrchestratorStoreFactoryTests`; SQLite
  behavior unchanged (228 Portal + 43 Orchestrator store/lease tests green).
- [x] **P1.2 Implement PostgreSQL for both stores (provider wired in P1.1; finished + verified here).**
  *(done)* Both Portal and Orchestrator state stores now run on PostgreSQL, verified end to end against
  a real Postgres via Testcontainers (`Category=Integration`). **Portal:** extracted the DbContext +
  entities + SQLite migrations into a new `ETL-SQL.ReportPortal.Data` library (breaks the EF
  multi-provider migrations cycle), added an `ETL-SQL.ReportPortal.Migrations.Postgres` assembly with a
  generated Postgres `InitialCreate`, and `PortalDatabase` selects it via `MigrationsAssembly`
  (`PortalPostgresProviderTests`). **Orchestrator:** refactored the 1150-line `SQLiteJobHistoryStore`
  to a provider-neutral `RelationalJobHistoryStore` behind an `IOrchestratorStoreDialect`
  (Sqlite/Npgsql); kept the SQL portable (`@`-params, `ON CONFLICT`, `COLLATE NOCASE` backed by a
  Postgres `nocase` ICU collation) with only the divergent bits (autoincrement, column-existence sweep,
  `RETURNING`) in the dialect; the factory + DI select the provider and the P1.1 fail-closed guards are
  lifted (`OrchestratorPostgresStoreTests`). SQLite remains the default and unchanged throughout
  (228 Portal + 127 Orchestration tests green).
- [x] **P1.3 Implement `etl-sql admin migrate-database --from sqlite --to postgres --dry-run`.**
  *(done)* New `admin migrate-database` subcommand copies both the Portal and Orchestrator SQLite state
  into the configured PostgreSQL deployment (`Portal:Database:ConnectionString` /
  `Orchestrator:Database:ConnectionString` from P1.1/P1.2). Lean, provider-neutral ADO copier
  (`DatabaseMigrationService`, App-only — no Portal reference): the target schema must pre-exist (Portal
  via EF migrations, Orchestrator via `InitializeAsync`), so this is a row copy, not DDL. Because EF maps
  the same model to different physical types per provider (bool→INTEGER vs `boolean`, DateTime/decimal/
  Guid→TEXT vs `timestamp`/`numeric`/`uuid`), every value is **coerced to the target column type**; FK
  ordering is bypassed for the load (`session_replication_role = replica`, fails closed with a clear
  message on a non-privileged role); identity/serial sequences are resynced afterward; per-table
  **row counts are verified** on both sides and any mismatch rolls that store's whole transaction back
  (fail closed). A live run preflights both stores before clearing either target. `--dry-run` verifies
  counts, value coercion, and target-schema compatibility without writing. Tests:
  `DatabaseMigrationServiceTests` (Category=Integration, Testcontainers) — type coercion + PK/sequence
  preservation, real Orchestrator identifier mapping, dry-run writes nothing, missing-target and
  incompatible-schema failures close safely. Builds on the v0.11.0 `admin` group.

### Phase 2 — Artifact Storage Abstraction

- [x] **P1.4 Create a unified storage-provider interface** for scripts, snapshots, cached datasets, and keys.
  *(done)* New `ETL_SQL.Core.Storage` contract: `IArtifactStorage` (provider-agnostic, async, stream-
  based) over an `ArtifactArea` enum (Scripts/Snapshots/Datasets/Maps/Keys, each its own root) with
  `ArtifactInfo` metadata. Operations were scoped from the actual filesystem calls in the Portal/Hosting
  services (exists, read stream/bytes/text, **atomic** write, move staging→final, delete, enumerate by
  prefix/recursion, stat) plus `LeaseLocalCopyAsync` for the path-based consumers (Parquet/Excel readers,
  connectors) — local providers return the real path, remote ones materialize a temp copy and clean it
  up on dispose; leasing is refused for the `Keys` area. Path normalization + traversal/absolute-path
  rejection live in `ArtifactPath.Normalize` (first guardrail line; P1.6 hardens the resolved path).
  Ships an `InMemoryArtifactStorage` reference implementation (executable spec + test double) with 14
  contract tests (`InMemoryArtifactStorageTests`). Local + SMB/UNC providers and call-site migration are
  P1.5; the `SecurityService`-backed guardrails are P1.6.
- [x] **P1.5 Implement Local and SMB/UNC shared storage providers** behind that interface.
  *(done)* `FileSystemArtifactStorage` is the shared System.IO engine (atomic write-temp-then-rename,
  owner-only `Keys`, resolved-path re-check via `SafePath`, no-op local lease); `LocalArtifactStorage`
  (default) and `SmbArtifactStorage` (UNC-root validation + fail-fast share reachability probe) are thin
  subclasses, selected by `ArtifactStorageFactory` ("local"/"smb"). The contract tests were refactored
  into a shared `ArtifactStorageContractTests` base run against **both** the in-memory reference and the
  filesystem provider (42 storage tests green), plus factory/SMB-validation tests. Wired into Portal DI
  as `IArtifactStorage` (config `Portal:Storage:Provider`, default Local; areas mapped to the existing
  root paths + the `.portal-keys` ring) — registration is inert until resolved, so portal boot is
  unchanged (52 portal config/integration tests green). **Call-site migration** off direct `File.*`/
  `Directory.*` is intentionally deferred: it lands incrementally alongside P1.6, once the
  `SecurityService`-backed guardrails are enforced at the storage boundary.
- [x] **P1.6 Enforce path-traversal guardrails and script immutability at the storage boundary.**
  *(done)* New `GuardedArtifactStorage` decorator wraps **any** `IArtifactStorage` so every provider
  inherits the guardrails at one chokepoint (not reimplemented per provider). It reuses
  `SecurityService`'s extension lists as the single source of truth — exposed as new predicates
  `IsDangerousExecutable` (the never-bypassable `BlockedExtensions` blacklist) and
  `IsApplicationLogicFile` (`BlockedWriteExtensions`) — and adds the area-aware policy the artifact areas
  need: **path traversal** rejected on every path (via `ArtifactPath.Normalize`); **no executables /
  code-signing files** (`.exe/.dll/.bat/.pfx/…`) writable to *any* area; **script immutability** —
  application-logic files (`.etlsql/.rptsql/.sql/.py/…`) writable only to the `Scripts` area, blocked in
  snapshots/datasets/maps/keys. Reads/enumerate/delete pass through (normalized); only create/replace
  (writes + a move's destination) are policy-checked. Wired into Portal DI so the configured provider is
  always wrapped by the guard. Tests: `GuardedArtifactStorageTests` runs the full shared
  `ArtifactStorageContractTests` (guard is transparent to legitimate ops) plus executable/logic-file/
  move-destination enforcement (74 storage tests green; 52 portal config/integration green).
  > **Follow-on (not a numbered phase): call-site migration.** Moving the ~43 direct `File.*`/
  > `Directory.*` consumers onto `IArtifactStorage` is a larger, behavior-affecting refactor (the dataset
  > catalog stores **absolute** `ParquetFilePath`s that backup/restore and maintenance read; migrating
  > means switching to area-relative keys). It should be done incrementally per service with its own
  > tests, not bundled into the guardrail work. The seam + guardrails are in place and DI-wired; consumers
  > adopt it service by service.

  > **Review findings to fix before the HA storage claim:** Phase 2 completed the provider seam and
  > guardrail boundary, but the storage provider is not yet authoritative for runtime artifacts.
  > - **[RESOLVED]** ~~Data Protection keys persist through `PersistKeysToFileSystem(.portal-keys)`,
  >   bypassing `IArtifactStorage`.~~ The key ring is now driven by `Portal:Storage:KeyRingPath` (defaults
  >   to node-local `.portal-keys`); the DP key ring and the Keys artifact area share one configurable
  >   root, so a multi-node deployment points every node at the same shared location.
  > - **[IN PROGRESS]** Portal/Hosting consumers still mostly use direct `File.*` / `Directory.*` paths.
  >   Migrating incrementally to area-relative `IArtifactStorage` keys, risk-ranked by stored-path model:
  >   - **Maps — DONE.** `GET /api/maps/custom` reads via the Maps area; relative client paths, no stored
  >     paths, so no data-model change. (Also fixed a latent bug: the storage singleton captured the
  >     startup `PortalConfig` local instead of resolving it from DI, ignoring later overrides.)
  >   - **Scripts — feasible, medium.** `ReportsController` stores **relative** `ScriptPath` against
  >     `ScriptRootPath`, so keys map directly — but the surface is large (content reads ×4, hash/staleness
  >     helper, the staged-write+backup+rollback save, upload, and `*.rptsql` listing) and the atomic-save
  >     logic is delicate. Reads are easy; the save path needs care.
  >   - **Snapshots — moderate.** `ExecutionJobService` writes snapshot HTML + manifest and stores
  >     `ManifestPath`; needs the write + stored-path + read side migrated together.
  >   - **Datasets — large, cross-tier.** Catalog stores **absolute** `ParquetFilePath` produced by
  >     **Engine** handlers (Create/Publish/Refresh, a lower tier with no Portal reference), encrypted at
  >     rest and read via decrypt-to-temp. Migrating means an absolute→relative data-model change touching
  >     Engine + Portal + backup/restore **and a data migration for existing rows** — its own work item,
  >     not a call swap. Needs explicit buy-in before starting.
  > - **[RESOLVED]** ~~`SmbArtifactStorage`'s reachability check allows startup when the UNC share root
  >   exists but the area subdirectory does not.~~ The check now probes the **share** (`\\server\share`)
  >   and fails fast only on an unreachable share; per-area subdirectories are created on demand by the
  >   first write (same as the local provider), with code/comments/tests aligned.
  > - **[RESOLVED in P1.9]** Concurrent Startup Migration Collisions — leader-elected migration lock.
  > - **[RESOLVED in P1.8]** Stale-Writer Collisions on Shared Storage — DB-backed write-epoch fencing.

### Phase 3 — Distributed Leases & Fencing

- [x] **P1.7 Database-backed node heartbeats + execution/job leases.**
  *(done)* New `INodeRegistryStore` (`Core.Data`) + `NodeHeartbeat` record generalize the per-job
  execution lease into a cluster-wide TTL heartbeat over shared state: `RegisterOrRenewNodeAsync`
  (upsert that preserves first-seen), `GetLiveNodesAsync` (now < ExpiresAt), `GetAllNodesAsync`,
  `DeregisterNodeAsync`, `PruneExpiredNodesAsync`. Implemented in the provider-neutral
  `RelationalJobHistoryStore` (new `Nodes` table + index; portable `@`-params / `ON CONFLICT` /
  ISO-8601 string times) so SQLite and PostgreSQL both inherit it, and registered as `INodeRegistryStore`
  in DI. New `NodeHeartbeatService` (`BackgroundService`, generalizing
  `SchedulerService.StartLeaseHeartbeat`) renews on `max(5, ttl/3)` and deregisters on graceful stop;
  all registry I/O is on the background loop and failures are swallowed so a degraded registry never
  takes down the host. Wired into both long-running hosts via `AddNodeHeartbeat(role)` — Orchestrator
  daemon ("Orchestrator") and Portal ("Portal") — but **not** `AddEtlSqlEngine`, so one-shot CLI never
  registers a node. Added `Microsoft.Extensions.Hosting.Abstractions` to the Orchestrator tier
  (first-party MIT; inventory refreshed). Tests: `NodeRegistryTests` (SQLite store + service
  register/renew/expiry/prune/deregister, first-seen preserved) and a PostgreSQL upsert/expiry case in
  `OrchestratorPostgresStoreTests` — 10 green; 132 orchestration unit + 65 portal integration/coordination
  tests still green. This is the substrate for fencing (P1.8) and leader election (P1.9).
- [x] **P1.8 Monotonically increasing fencing tokens** to reject stale writers during partitions (Gap #5).
  *(done)* **Database state writes:** each successful job-lease acquisition stamps the job with a strictly
  increasing fence token (new `LeaseFenceToken`; a renewal does not advance it). `AcquireJobLeaseAsync`
  returns the token; the durable completion write `TryUpdateJobLastRunFencedAsync` carries it and the store
  rejects it (zero rows) once a newer owner has advanced the token, so a paused-then-resumed node can't
  clobber the newer owner's scheduling state. `SchedulerService` captures the token at acquire and uses the
  fenced write (BLOCKED + normal next-run), logging when fenced out; `TryAcquireJobLeaseAsync` delegates to
  the new acquire. **Shared storage writes (SMB/UNC):** since SMB/UNC has no native fencing, a new
  DB-backed `IWriteEpochStore` makes the shared database the fencing authority — `TryClaimWriteEpochAsync`
  is an atomic compare-and-advance (conditional `ON CONFLICT … DO UPDATE … WHERE`), and a new
  `FencedArtifactStorage` decorator claims an artifact's write epoch (keyed by area+path, token from the
  node's fence-token supplier) before every write/move-destination, throwing `FencedWriteException` on a
  stale token. Both implemented portably in `RelationalJobHistoryStore` (new `LeaseFenceToken` + `WriteEpochs`
  table) and registered in DI. Tests: `FencingTokenTests`, `WriteEpochFencingTests`
  (CAS + decorator stale-writer/move-destination) + PostgreSQL fencing & write-epoch cases in
  `OrchestratorPostgresStoreTests`; mock-based scheduler tests updated — 135 orchestration unit + 7 Postgres
  integration + 51 portal integration green. Builds on the P1.7 lease/heartbeat substrate.
- [x] **P1.9 Database-backed leader election** for cluster singletons (e.g. running migrations once).
  *(done)* New `IClusterLockStore` (Core.Data) — a TTL-leased named lock (= leader election; the holder is
  the leader): `TryAcquireLockAsync` (atomic claim via conditional `ON CONFLICT … WHERE expired OR same
  owner`), `TryRenewLockAsync`, `ReleaseLockAsync`, `GetLockHolderAsync`. Implemented portably in
  `RelationalJobHistoryStore` (new `ClusterLocks` table) — which is a `CREATE TABLE IF NOT EXISTS` store,
  so the lock exists **before** any node runs EF migrations (no chicken-and-egg). New `ClusterLock.
  RunExclusiveAsync` helper block-acquires the lock (fail-fast `TimeoutException` past the wait window),
  auto-renews on a background heartbeat so a long critical section can't let the lease lapse, runs the
  action once, and always releases. **Wired into the Portal boot migration**: concurrent Portal nodes now
  serialize through the `portal-db-migration` lock — the leader applies migrations while the others wait,
  then find nothing pending and no-op, fixing the *Concurrent Startup Migration Collisions* review finding.
  Tests: `ClusterLockTests` (single-holder/contention/expiry/renew/release, `RunExclusiveAsync` serializes
  5 racing nodes to `maxConcurrent==1`, timeout when held throughout) + a PostgreSQL lock case in
  `OrchestratorPostgresStoreTests` — 13 green; 55 portal integration (incl. upgrade-path + migration-
  convergence drills) green. Builds on the P1.7/P1.8 coordination substrate.

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
