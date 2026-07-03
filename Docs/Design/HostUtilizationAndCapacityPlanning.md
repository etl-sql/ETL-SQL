# Host Utilization Time Series & Capacity Reporting — Implementation Plan

> **Status:** implementation plan (drafted 2026-07-02); most of it has now shipped. The
> host-utilization **time series + read surface**, the **daily roll-up summary** (JobHistory + host
> metrics), and the **capacity report** template are done (see the ✅ markers in *Sequencing*). Remaining:
> the `backup_and_report.etlsql` template, whole-host CPU probes, and the Portal operational-metrics
> subscription. Covers `TODO.md` → *Administrator operational review — follow-on hardening*. Grounded in
> the code investigated during the 2026-07-01/02 session; file/method references below are current as of then.

## What already exists (do not rebuild)

- **`NodeCapacityMonitor.Capture()`** (`ETL-SQL.Orchestrator/Scheduling/NodeCapacityMonitor.cs`)
  returns a `NodeCapacitySnapshot`: process working set, GC heap, total available memory, **host
  memory-load %**, **process CPU %**, processor count, `IsOverloaded`, and (new this session)
  **`StateDiskFreeBytes` / `SpillDiskFreeBytes`** via `DriveInfo`. Point-in-time; 1-second cache.
- **`NodeHeartbeatService`** samples `Capture()` each heartbeat and writes it as JSON `metadata` on the
  node registry row via `RegisterOrRenewNodeAsync` — but this is **latest-per-node only** (register-or-
  renew overwrites), not a time series.
- **Retention pattern to copy:** `IJobHistoryStore.PruneHistoryAsync(maxAge)` + the scheduler's
  periodic maintenance block (`SchedulerService.RunAsync`, guarded by `Scheduler:HistoryPruneIntervalMinutes`
  / `Orchestrator:JobHistoryRetentionDays`). The same shape works for host-metrics retention.
- **Store schema pattern:** `RelationalJobHistoryStore` (base) + `SQLiteJobHistoryStore` (SQLite
  subclass); schema created in `InitializeAsync`, timestamps stored as round-trip `"O"` strings, one
  implementation covers SQLite and Postgres. Adding a table here reaches both providers.
- **Read-into-script pattern:** `SHOW JOB HISTORY [INTO #t]` reads the local `IJobHistoryStore` and
  supports `INTO`, so a report/template can `SELECT ... FROM #t`. This is how the capacity report
  template will consume the data with no new query API.

## The gap

1. No **persisted time series** of host utilization (only the latest snapshot per node).
2. No **read surface** for it (script or API).
3. **CPU is process-level, not whole-host** — the process CPU % undercounts a busy box.
4. No **roll-up**: raw rows (job history and, once added, host metrics) can't be pruned without losing
   long-term trend, so capacity planning loses history at the retention boundary.

## Design

### 1. `HostMetrics` table (orchestrator store)

Add to `RelationalJobHistoryStore.InitializeAsync` (idempotent `CREATE TABLE IF NOT EXISTS`):

| Column | Type | Notes |
| :--- | :--- | :--- |
| `Id` | INTEGER PK | |
| `NodeId` | TEXT | from `NodeHeartbeatService.NodeId` |
| `CapturedAt` | TEXT (`"O"`) | sample time |
| `MemoryLoadPercent` | REAL | host memory load |
| `ProcessCpuPercent` | REAL | process CPU (rename-safe: keep until host CPU lands) |
| `HostCpuPercent` | REAL NULL | whole-host CPU, null until item 3 ships |
| `StateDiskFreeBytes` | INTEGER | |
| `SpillDiskFreeBytes` | INTEGER | |

Index `(NodeId, CapturedAt)`. Interface additions on `IJobHistoryStore` (or a new focused
`IHostMetricsStore` — preferred, to keep `IJobHistoryStore` cohesive):
`AppendHostMetricsAsync(snapshot, nodeId)`, `GetHostMetricsAsync(nodeId?, since, limit)`,
`PruneHostMetricsAsync(maxAge)`, `RollUpHostMetricsAsync(...)` (see item 4).

### 2. Sampler + retention

Sample from **`NodeHeartbeatService`** (it already calls `Capture()` every beat) — append a
`HostMetrics` row right after `RegisterOrRenewNodeAsync`. Rate: heartbeat cadence is fine (≈ttl); if
too coarse, add a dedicated `HostMetricsSamplerService : IHostedService` on a `Scheduler:HostMetricsIntervalSeconds`
timer (default 60). Retention: extend the existing scheduler maintenance block with
`PruneHostMetricsAsync(Orchestrator:HostMetricsRetentionDays, default 14)` — raw samples are dense, so
retain them shorter than JobHistory and rely on the roll-up for long-term trend.

### 3. Whole-host CPU (optional, platform-specific)

Process CPU is a floor, not the box's load. For true host CPU:
- **Windows:** `PerformanceCounter("Processor", "% Processor Time", "_Total")` (needs a warm-up read).
- **Linux:** delta of `/proc/stat` `cpu` line between samples.
- **macOS:** `host_statistics` / `top` parse — lowest priority.

Isolate behind an `IHostCpuProbe` with a no-op default that returns null (so `HostCpuPercent` stays
null and callers fall back to `ProcessCpuPercent`). Ship platform probes incrementally; never let a
probe failure break sampling.

### 4. Roll-up summary (covers the JobHistory roll-up item too) — ✅ SHIPPED

Two daily-aggregate tables, written on the scheduler maintenance cycle and retained far longer
(`Orchestrator:HistoryRollupRetentionDays`, default 400 days) than raw rows:
- **`JobHistoryDaily`** (PK `(Day, JobName)`): run count, failure count (`Status <> 'SUCCESS'` over
  completed rows — the roll-up excludes in-flight `RUNNING`), total rows, max peak memory.
- **`HostMetricsDaily`** (PK `(Day, NodeId)`): avg/max memory-load %, avg/max CPU %, min free disk
  (state/spill). Min free disk is the saturation signal.

Day is derived portably as `substr(<timestamp>, 1, 10)` (the `"O"` round-trip strings sort/prefix as
`yyyy-MM-dd` on both SQLite and Postgres). Each roll-up is **idempotent and transactional**: within one
transaction it `DELETE`s the summary rows for every day still present in the raw table, then re-`INSERT`s
them from a `GROUP BY` — so re-running never double-counts and a day is only ever fully recomputed.

Roll-up runs **before** raw pruning (`RollUpJobHistoryAsync`/`RollUpHostMetricsAsync` precede
`PruneHistoryAsync`/`PruneHostMetricsAsync` in the maintenance block), so rows about to age out are
captured first and trend survives pruning. Summaries are pruned on their own long horizon via
`PruneJobHistoryDailyAsync`/`PruneHostMetricsDailyAsync`. Read via
`GetJobHistoryDailyAsync`/`GetHostMetricsDailyAsync`. *(store test: aggregation + idempotency + retention)*

### 5. Read surface

Prefer **no new query API** — expose the tables so the capacity-report template (below) can read them:
- Simplest: a `SHOW HOST METRICS [INTO #t]` statement mirroring `SHOW JOB HISTORY` (reuse
  `ShowJobHistoryStatementHandler` shape; add AST node, parser case, handler reading `IHostMetricsStore`).
- Or an orchestrator `GET /api/host-metrics` endpoint + the orchestrator connector, mirroring
  `api/history` / `FetchJobHistoryAsync`, for cross-node aggregation from the Portal.
Ship `SHOW HOST METRICS` first (local, no HTTP); add the endpoint only if cross-node roll-up is needed.

## Remaining admin template scripts (build on the above)

Location: `samples/admin_operations/` (alongside `daily_failure_digest.etlsql`). Verify each the same
way the digest was — an in-process test that seeds the store, runs the script's core, and a full-file
parse check. Watch the two gotchas found this session: the `SEND EMAIL` connection clause is
**`AT connectionName`** (not `@`), and orchestrator statuses are **`SUCCESS`/`FAILURE`/`BLOCKED`/
`QUARANTINED`/`RUNNING`/`INTERRUPTED`**.

1. **`backup_and_report.etlsql`** — invoke the existing `etl-sql admin backup` externally (OS scheduler)
   and this script records the outcome + emails on failure. Note: backup is a CLI, not an in-language
   statement (deliberately — no in-language `BACKUP` that appears to cover Postgres). The template's
   job is status capture + alert, not running the backup itself. Simplest form: the OS scheduler runs
   `admin backup` then runs this script with the exit code as a parameter; the script writes a
   pass/fail marker (`SET_JOB_STATE`) and `SEND EMAIL` on failure. Confirm `@@`-param passing and
   `SET_JOB_STATE` semantics before finalizing.
2. **`capacity_report.etlsql`** — `SHOW JOB HISTORY INTO #jh` (+ `SHOW HOST METRICS INTO #hm` once
   item 5 ships), aggregate per job/day (counts, failures, peak memory, avg duration) and per node
   (min free disk, peak memory/CPU), and either `SEND EMAIL` a summary or write a report the Portal
   renders. The JobHistory half is buildable today; the host half waits on items 1–5.
3. **Operational-metrics email** — a Portal subscription over `OperationalMetricsService` data
   (active/queued, 24h failure rate, storage bytes). This is Portal-side, not orchestrator; likely a
   report + subscription rather than a script.

## Sequencing

1. ✅ **DONE** (`7a5d4bd2`) — `HostMetrics` table + `IHostMetricsStore` + append from heartbeat + retention.
2. ✅ **DONE** (`b18e48d3`) — `SHOW HOST METRICS [nodeId] [INTO]` read surface.
3. ✅ **DONE** — Roll-up tables (`JobHistoryDaily`/`HostMetricsDaily`) + idempotent daily aggregation +
   long retention (`Orchestrator:HistoryRollupRetentionDays`, default 400). *(covers the JobHistory roll-up item)*
4. `capacity_report.etlsql` ✅ **DONE** (`samples/admin_operations/`); `backup_and_report.etlsql` remains
   — each verified in-process. *(read surfaces `SHOW JOB HISTORY` + `SHOW HOST METRICS` both exist)*
5. Whole-host CPU probes (Windows, then Linux), incrementally — fills `HostMetrics.HostCpuPercent`
   (currently null; the column, store, and `SHOW HOST METRICS` output already carry it).
6. Operational-metrics Portal subscription. *(Portal-side, independent)*

## Guardrails

- Best-effort everywhere: a metrics/disk/CPU probe failure must never break sampling, a heartbeat, or a
  job. (Follow the `TryGetFreeBytes` pattern.)
- Retention must not orphan roll-ups: aggregate **before** deleting raw rows.
- Keep `IJobHistoryStore` cohesive — put host metrics on a separate `IHostMetricsStore`.
- Every template ships with an in-process mechanic test + a full-file parse check (the parse check
  already caught a real bug once).
