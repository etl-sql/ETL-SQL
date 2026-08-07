# ETL-SQL Orchestrator Architecture & Engineering Reference

**Applies to ETL-SQL 0.18.0**

This document describes the internal mechanics of the `ETL-SQL.Orchestrator` project — the layer responsible for scheduling, executing, tracking, and governing ETL-SQL scripts in both interactive (TUI/IDE) and unattended (background job) contexts. It is the primary reference for engineers who need to understand how scripts move from a `CREATE JOB` statement to a completed run entry in `eng.job_history`.

---

## 1. Architecture Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                      Host layer                                    │
│  ETL-SQL.App (CLI/TUI) ◄────────────────────────────────────────┐  │
│  ETL-SQL-OrchestratorService (background Windows Service/daemon)│  │
└──────────────────────────┬──────────────────────────────────────┘  │
                           │ triggers via                            │
                           ▼                                         │
┌────────────────────────────────────────────────────────────────────┤
│                   ETL-SQL.Orchestrator                             │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  SchedulerService                                            │  │
│  │  • Polls SQLiteJobHistoryStore every 30 seconds              │  │
│  │  • Finds active jobs whose NextRun ≤ now                     │  │
│  │  • Acquires concurrency slot via JobThrottle                 │  │
│  │  • Delegates to IScriptExecutor                              │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                           │                                        │
│            ┌──────────────┴──────────────────┐                     │
│            ▼                                 ▼                     │
│  ┌──────────────────────┐      ┌─────────────────────────────┐     │
│  │ ScriptExecutorAdapter│      │ ProcessJobExecutor          │     │
│  │ (default / in-proc)  │      │ (optional / out-of-proc)    │     │
│  │                      │      │                             │     │
│  │ wraps                │      │ spawns ETL-SQL.exe run <f>  │     │
│  │ ExecutionSession     |      │ --json as child process     │     │
│  └──────────────────────┘      └─────────────────────────────┘     │
│            │                                                       │
│            ▼                                                       │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │  ExecutionSession                                          │    │
│  │  1. Lex  →  2. Parse  →  3. Lint  →  4. Evaluate           │    │
│  │  Persistent connections & variables across IDE F5 runs     │    │
│  └────────────────────────────────────────────────────────────┘    │
│            │                                                       │
│            ▼                                                       │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │  ETL-SQL.Engine (Evaluator, Handlers, Connectors)          │    │
│  └────────────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────────────┘

Storage:
  SQLiteJobHistoryStore  →  etlsql.db (Jobs + JobHistory tables)
  ChildProcessTracker    →  logs/child-pids.json (crash recovery)
```

### 1.1 Project dependencies

```
ETL-SQL.Orchestrator
  └── ETL-SQL.Engine
        └── ETL-SQL.Core
              └── ETL-SQL.Data (interfaces)
                  ETL-SQL.Common (ILogger, CliContext)
```

External NuGet packages:
- `Microsoft.Data.Sqlite` 9.x — job history persistence
- `Microsoft.Extensions.DependencyInjection` 10.x — service scope management
- `Microsoft.Extensions.Logging.Abstractions` 10.x — structured logging contracts

---

## 2. Session Lifecycle — `ExecutionSession`

`ExecutionSession` is the central coordination object for interactive script execution.  It is used by both the TUI (Terminal IDE) and the CLI host. Its defining feature is **state persistence across runs**: connections created in one F5 execution survive into the next, exactly as a user would expect from a REPL.

### 2.1 Lifecycle phases

```
┌─────────────────────────────────────────────────────────────────┐
│  ExecutionSession lifetime (matches TUI window session)         │
│                                                                 │
│  ctor(serviceProvider, CliContext, ILogger)                     │
│    → creates _persistentConnections (ConcurrentDictionary)      │
│    → creates _persistentVariables (VariableScopeManager)        │
│                                                                 │
│  ─────────────────────────────────────────────────────────      │
│  First F5 execution:                                            │
│   ExecuteAsync(source)                                          │
│     Phase 1 — Lex                                               │
│       Lexer.Tokenize(source) → List<Token>                      │
│     Phase 2 — Parse                                             │
│       Parser.Parse(tokens, source) → Script AST                 │
│       Script.Diagnostics populated with parse errors            │
│       ← ABORT if any Severity == Error                          │
│     Phase 3 — Lint                                              │
│       LinterFactory.CreateWithAllRules().AnalyzeAsync(script)   │
│       ← ABORT if any LintSeverity == Error                      │
│     Phase 4 — Evaluate                                          │
│       Evaluator injected with _persistentConnections +          │
│         _persistentVariables                                    │
│       evaluator.Evaluate(script, cancellationToken)             │
│     → ExecutionResult populated & returned                      │
│                                                                 │
│  Subsequent F5 runs reuse the same session:                     │
│    _persistentConnections → connections from run N available    │
│    _persistentVariables   → variables from run N available      │
│                                                                 │
│  ─────────────────────────────────────────────────────────      │
│  Session end (TUI exit / IDE close):                            │
│   DisposeAsync()                                                │
│    → disposes _lastEvaluator (closes any open reader/writer)    │
│    → disposes all _persistentConnections (releases ADO.NET      │
│        connection pool slots, closes file handles)              │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 `ExecutionResult` structure

`ExecutionSession.ExecuteAsync` returns an `ExecutionResult` containing:

| Field | Type | Description |
|---|---|---|
| `Success` | `bool` | `true` only if all phases passed without error |
| `Diagnostics` | `List<Diagnostic>` | Parse errors and warnings |
| `LintResults` | `List<LintResult>` | Lint findings from all rules |
| `ResultsTables` | `List<DataTable>` | Result sets emitted by `SELECT` statements |
| `ExecutionTree` | `ExecutionTree` | Node tree of every statement's status and timing |
| `Messages` | `List<string>` | `PRINT` output and informational messages |
| `RowsProcessed` | `long` | Aggregate row count across all DML operations |
| `ActiveConnections` | `Dictionary<string, IDataSource>` | Live connection snapshot for IDE autocomplete |
| `ExecutionTimeMs` | `long` | Wall-clock time for the entire `ExecuteAsync` call |

### 2.3 Security override flags

Zero-Trust security guardrails can be overridden using session-scoped `SET` statements. These overrides are only authorized if the executing script's path is located within an approved safe zone:

| Statement override | Effect |
|---|---|
| `SET ALLOW_FILE_TYPE_ACCESS = ON` | Allows non-standard file extensions in `FLATFILE` connectors |
| `SET ALLOW_FILE_TYPE_ACCESS = '.ext'` | Whitelists a specific file extension for the session |
| `SET ALLOW_FILE_OPERATIONS = n` | Overrides the runaway file-operation count limit (default 100) |
| `SET ALLOW_RECURSIVE_LAYERS = n` | Overrides the `RUN SCRIPT` recursion nesting depth limit (default 5) |

> [!CAUTION]
> These overrides bypass safety boundaries. They must only be used in scripts stored in `ApprovedSafeZones`. See `SecurityService.cs` for zone management.

### 2.4 Live execution tree callback

The TUI uses the `OnTreeNodeAdded` callback to update the execution tree panel in real time:

```csharp
session.OnTreeNodeAdded = nodeName => treePanel.AddNode(nodeName);
```

This is wired before the first `ExecuteAsync` call and invoked each time the `Evaluator` appends a new node to its `ExecutionTree`.

---

## 3. Job Scheduling — `SchedulerService`

`SchedulerService` runs a continuous background loop that drives all `CREATE JOB` definitions registered at the engine layer.

### 3.1 Scheduler loop

```
SchedulerService.Start()
  └── Task.Run(RunAsync(CancellationToken))

RunAsync():
  1. store.InitializeAsync()          — creates Jobs + JobHistory tables if absent
  2. LOOP every 30 seconds:
       a. store.GetActiveJobsAsync()  — SELECT * FROM Jobs WHERE IsEnabled = 1
       b. For each job where NextRun ≤ DateTime.Now:
            ExecuteJobAsync(job)
       c. Task.Delay(30s, ct)         — cooperative cancellable sleep

SchedulerService.Stop()
  └── _cts.Cancel()                   — cancels the delay; loop exits cleanly
```

### 3.2 Job execution flow

```
ExecuteJobAsync(job):
  0. capacityMonitor.Capture()
       → overloaded node = skip this cycle without claiming the lease

  1. store.TryAcquireJobLeaseAsync(job.Name, ownerId, leaseDuration)
       → single atomic UPDATE: claim succeeds only if the lease is free or expired
       → not acquired = another scheduler instance owns this occurrence → skip
       → a heartbeat task renews the lease at leaseDuration/3 for the whole run;
         losing the lease cancels the run; the lease is released after completion

  2. store.LogJobStartAsync(job.Name)
       → INSERT INTO JobHistory (JobName, StartTime, Status='RUNNING')
       → returns historyId (autoincrement primary key)

  3. throttle.AcquireAsync(job.Name)
       → waits on SemaphoreSlim if MaxConcurrentJobs cap is reached
       → returns IDisposable slot (auto-releases on dispose)

  4. serviceProvider.CreateScope()
       → creates DI child scope for full resource isolation

  5. scope.GetRequiredService<IScriptExecutor>()
       → resolves ScriptExecutorAdapter (default) or ProcessJobExecutor
          depending on ProcessJobExecutorOptions.UseProcessSpawning

  6. executor.ExecuteTextAsync(job.Script)
       → ScriptExecutionResult { Success, RowsProcessed, ErrorMessage }

  7. store.LogJobEndAsync(historyId, status, errorMessage, rowsProcessed)
       → UPDATE JobHistory SET EndTime, Status, ErrorMessage, RowsProcessed

  8. store.UpdateJobLastRunAsync(job.Name, now, nextRun)
       → CalculateNextRun(job) based on Interval + Unit + AtTime
       → UPDATE Jobs SET LastRun, NextRun

  9. quarantine policy
       → if the most recent Scheduler:QuarantineFailureThreshold history rows are failures,
         disable the job and write a QUARANTINED history row
```

### 3.3 `NextRun` calculation

| `Unit` | Calculation |
|-------|-------------|
| `SECOND` | `now + Interval seconds` |
| `MINUTE` | `now + Interval minutes` |
| `HOUR` | `now + Interval hours` |
| `DAY` | `now + Interval days`, then snapped to `AtTime` if specified |
| *(unrecognized)* | `now + 1 hour` (safe fallback) |

When `AtTime` is set (e.g., `'22:00'`) and `Unit = DAY`, the next run is calculated as midnight of the next day + the AtTime offset, ensuring daily jobs always fire at the correct wall-clock time even if the previous run ended late.

### 3.4 Execution lease (duplicate-run prevention)

Every run — scheduled or manually triggered — first claims a per-job lease stored in the `Jobs` row (`LeaseOwner`, `LeaseExpiresAt`, UTC ISO-8601). The claim is one atomic `UPDATE ... WHERE` lease-free-or-expired, riding the configured relational job store's write guarantees, so **two scheduler processes sharing one job DB produce exactly one execution per due occurrence**. The owner id is `machine:pid:guid`, unique per process start. A heartbeat renews the lease at one-third of `Scheduler:JobLeaseSeconds` (default 600s; floor 30s); if renewal fails because the lease expired and was reclaimed, the run cancels itself rather than risk a duplicate. A lease abandoned by a crash self-heals at expiry and the occurrence reruns — at-least-once semantics. For multi-node HA posture and failure certification, see [HA Topology Failure Certification](decisions/HA_Topology_Failure_Certification.md).

### 3.5 Node capacity and quarantine

`NodeHeartbeatService` writes CPU and memory capacity metadata into the shared `Nodes.Metadata`
JSON on every heartbeat: process working set, GC heap bytes, available memory, memory load percent,
process CPU percent, processor count, and `IsOverloaded`. The scheduler uses the same
`INodeCapacityMonitor` locally before it claims a job lease. If the node is overloaded, it skips
the claim for that cycle so another healthy node can acquire the work.

After each execution cycle, `SchedulerService` reads the latest history rows for the job. If the
latest `Scheduler:QuarantineFailureThreshold` rows are all failures, the scheduler saves the job as
disabled and appends a `QUARANTINED` history row. Set `Scheduler:QuarantineFailureThreshold` to `0`
to disable automatic quarantine; the default is `5`.

### 3.6 Concurrency metrics

```csharp
JobThrottleMetrics metrics = schedulerService.GetMetrics();
// metrics.ActiveJobs    — currently executing jobs
// metrics.QueuedJobs    — waiting for a slot
// metrics.MaxJobs       — configured cap (0 = auto)
// metrics.AvailableSlots — remaining semaphore count
```

---

## 4. Concurrency — `JobThrottle`

`JobThrottle` enforces a configurable cap on concurrently executing jobs using a `SemaphoreSlim`. Jobs that exceed the cap are **queued**, not rejected.

### 4.1 Slot lifecycle

```
throttle.AcquireAsync(jobName):
  1. Interlocked.Increment(_queuedJobs)
  2. await _semaphore.WaitAsync(ct)      — blocks here if cap reached
  3. Interlocked.Decrement(_queuedJobs)
  4. Interlocked.Increment(_activeJobs)
  5. returns Slot (IDisposable)

Slot.Dispose():
  1. Interlocked.Decrement(_activeJobs)
  2. _semaphore.Release()                — unblocks next waiting AcquireAsync
```

### 4.2 Configuration

`JobThrottle` is configured via `JobThrottleOptions`, bound from `appsettings.json`:

```json
{
  "Jobs": {
    "MaxConcurrentJobs": 4
  }
}
```

When `MaxConcurrentJobs` is `0` (the default), the cap auto-sizes to `Math.Max(1, ProcessorCount / 2)`.

---

## 5. Script Execution Adapters

The scheduler dispatches through the `IScriptExecutor` interface, allowing two concrete strategies:

```csharp
public interface IScriptExecutor
{
    Task<ScriptExecutionResult> ExecuteTextAsync(string scriptText,
        CancellationToken cancellationToken = default);
}

public record ScriptExecutionResult(bool Success, long RowsProcessed, string? ErrorMessage);
```

### 5.1 `ScriptExecutorAdapter` — in-process (default)

Used when `ProcessJobExecutorOptions.UseProcessSpawning = false` (the default).

```
ScriptExecutorAdapter.ExecuteTextAsync(script):
  1. new ExecutionSession(serviceProvider, cliContext, logger)
  2. session.ExecuteAsync(script) → ExecutionResult
  3. maps to ScriptExecutionResult:
       Success      = result.Success
       RowsProcessed = result.RowsProcessed
       ErrorMessage  = joined Diagnostics if !Success
```

**Characteristics:** lowest overhead; shares the orchestrator process heap; connections and engine state are isolated per DI scope.

### 5.2 `ProcessJobExecutor` — out-of-process (optional)

Used when `ProcessJobExecutorOptions.UseProcessSpawning = true`.

```
ProcessJobExecutor.ExecuteTextAsync(script):
  1. Write script to a temp .etlsql file in %TEMP%
  2. Spawn: ETL-SQL.exe run "<tempfile>" --json
  3. Capture stdout + stderr asynchronously
  4. Enforce per-job timeout (default 3600 seconds)
     → on timeout: Process.Kill(entireProcessTree: true)
  5. Parse JSON envelope from last line of stdout:
       { "success": bool, "rowsProcessed": long, "error": string }
  6. Fallback to exit code 0/non-zero if no JSON found
  7. Delete temp file (best-effort)
```

**Characteristics:** full memory isolation; orphaned child processes are tracked by `ChildProcessTracker` and killed on Orchestrator restart; supports jobs compiled against a different engine version.

### 5.3 Choosing between adapters

| Scenario | Recommended adapter |
|---|---|
| Single-server deployment, normal jobs | `ScriptExecutorAdapter` (default) |
| Jobs that may exhaust memory or leak handles | `ProcessJobExecutor` |
| Multi-version engine support | `ProcessJobExecutor` |
| Local development / testing | `ScriptExecutorAdapter` |

---

## 6. Job History Store — `SQLiteJobHistoryStore`

`SQLiteJobHistoryStore` implements `IJobHistoryStore` using a local SQLite database (`etlsql.db` by default).

### 6.1 Schema

```sql
CREATE TABLE IF NOT EXISTS Jobs (
    Name               TEXT PRIMARY KEY,
    Script             TEXT NOT NULL,
    Interval           INTEGER NOT NULL,    -- numeric magnitude
    Unit               TEXT NOT NULL,       -- 'SECOND'|'MINUTE'|'HOUR'|'DAY'
    AtTime             TEXT,                -- optional wall-clock anchor, e.g. '22:00'
    LastRun            TEXT,                -- ISO-8601 or NULL
    NextRun            TEXT,                -- ISO-8601 or NULL (NULL = run immediately)
    IsEnabled          INTEGER NOT NULL DEFAULT 1,
    MaxRetries         INTEGER NOT NULL DEFAULT 0,
    RetryDelaySeconds  INTEGER NOT NULL DEFAULT 30,
    ScriptHash         TEXT,                -- 'sha256:<hex>' of Script bytes at CREATE JOB time
    HashPolicy         TEXT NOT NULL DEFAULT 'Warn',  -- 'Warn'|'Block'
    LeaseOwner         TEXT,                -- execution lease holder ('machine:pid:guid') or NULL
    LeaseExpiresAt     TEXT                 -- UTC ISO-8601 lease expiry; reclaimable when past
);

CREATE TABLE IF NOT EXISTS JobHistory (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    JobName             TEXT NOT NULL,
    StartTime           TEXT NOT NULL,   -- ISO-8601
    EndTime             TEXT,            -- NULL while RUNNING
    Status              TEXT NOT NULL,   -- 'RUNNING'|'SUCCESS'|'FAILURE'|'BLOCKED'
    ErrorMessage        TEXT,
    RowsProcessed       INTEGER DEFAULT 0,
    PeakMemoryBytes     INTEGER DEFAULT 0,
    CpuTimeSeconds      REAL DEFAULT 0,
    ScriptHashAtRunTime TEXT,            -- hash computed at execution time
    HashMatched         INTEGER          -- 1=match, 0=mismatch, NULL=no pinned hash
);
```

### 6.2 `IJobHistoryStore` interface

```csharp
public interface IJobHistoryStore
{
    Task InitializeAsync();
    Task SaveJobAsync(JobDefinition job);
    Task<JobDefinition?> GetJobAsync(string name);
    Task<IEnumerable<JobDefinition>> GetActiveJobsAsync();
    Task<IEnumerable<JobDefinition>> GetAllJobsAsync();
    Task DeleteJobAsync(string name);
    Task UpdateJobLastRunAsync(string name, DateTime lastRun, DateTime? nextRun);
    Task<bool> TryAcquireJobLeaseAsync(string jobName, string owner, TimeSpan duration);
    Task<bool> TryRenewJobLeaseAsync(string jobName, string owner, TimeSpan duration);
    Task ReleaseJobLeaseAsync(string jobName, string owner);
    Task<long> LogJobStartAsync(string jobName);
    Task LogJobEndAsync(long entryId, string status, string? errorMessage = null,
        long rowsProcessed = 0, long peakMemoryBytes = 0, double cpuTimeSeconds = 0,
        string? scriptHashAtRunTime = null, bool? hashMatched = null);
    Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(string? jobName = null, int limit = 100);
}
```

`JobDefinition` and `JobHistoryEntry` are positional record types defined in `ETL-SQL.Core/Data/IJobHistoryStore.cs`.

`JobDefinition` carries two hash-pinning fields: `ScriptHash` (stored at `CREATE JOB` time as `"sha256:<hex>"`) and `HashPolicy` (`"Warn"` or `"Block"`). `JobHistoryEntry` carries `ScriptHashAtRunTime` and `HashMatched` so every execution is auditable regardless of whether the hash matched.

### 6.3 Script hash integrity

When a job is created via `CREATE JOB`, `CreateJobStatementHandler` computes a SHA-256 hash of the script text and stores it as `ScriptHash` in the `Jobs` table. At execution time, `SchedulerService.ExecuteJobAsync` recomputes the hash and compares it:

- **Match**: execution proceeds; `HashMatched = 1` is recorded in `JobHistory`.
- **Mismatch + `HashPolicy = 'Warn'`** (default): a warning is logged and execution continues; `HashMatched = 0` is recorded.
- **Mismatch + `HashPolicy = 'Block'`**: a `BLOCKED` history entry is written and the job is skipped for this run cycle.

The default policy is controlled by `Engine:ScriptHashPolicy` in `appsettings.json` and may be overridden per-session with:

```sql
SET SCRIPT_HASH_POLICY = 'Block';
```

This guards against out-of-band modifications to the `Script` column in `etlsql.db` (e.g., direct SQLite edits). It does not provide cryptographic key management or signing — the hash is advisory, not a trust anchor.

### 6.4 Engine integration

`CREATE JOB` and `DROP JOB` statements in `ETL-SQL.Engine` call `IJobHistoryStore` directly through the `IExecutionContext`. `eng.jobs` and `eng.job_history` read from the same store. This means the engine language layer and the Orchestrator share the same SQLite database and the same `IJobHistoryStore` abstraction — there is no separate API or message bus between them.

---

## 7. Channel API — `IJobChannel`

The `IJobChannel` interface provides a transport-agnostic way to submit ad-hoc scripts for immediate execution (i.e., not via the scheduler). Two implementations exist:

### 7.1 `InProcessJobChannel`

Used for local development or when the Orchestrator Service is not running as a separate process. Jobs execute directly via `IScriptExecutor` in the current process.

```
InProcessJobChannel.SubmitJobAsync(request):
  1. Generates a short job ID (8-char hex)
  2. Stores a JobEntry { Status=Queued, CancellationTokenSource }
  3. Fires-and-forgets RunJobAsync() on a background task
  4. Returns jobId immediately

RunJobAsync():
  entry.Status = Running
  result = await executor.ExecuteTextAsync(scriptText, ct)
  entry.Status = Completed | Failed | Cancelled
  entry.RowsProcessed, ErrorMessage captured
```

### 7.2 `HttpJobChannelClient`

Used in production when ETL-SQL-OrchestratorService runs as a separate process or on a different host. Sends requests to the Orchestrator's REST API. See `Channels/HttpJobChannelClient.cs` for the endpoint contract.

### 7.3 Status model

```
JobRunStatus:  Queued → Running → Completed
                                → Failed
                                → Cancelled
```

---

## 8. Child Process Safety — `ChildProcessTracker`

When `ProcessJobExecutor` is in use, `ChildProcessTracker` ensures that no orphaned child processes survive an Orchestrator crash.

### 8.1 Normal operation

```
ProcessJobExecutor spawns process (PID = N):
  tracker.Register(N, scriptPath)
    → _active[N] = scriptPath
    → Persist() — writes logs/child-pids.json

Process exits normally:
  tracker.Unregister(N)
    → _active.TryRemove(N)
    → Persist()
```

### 8.2 Crash recovery

```
Orchestrator restarts:
  tracker.CleanupOrphans()
    → reads logs/child-pids.json
    → for each recorded PID:
         if process still alive → Process.Kill(entireProcessTree: true)
    → deletes child-pids.json
```

The PID file uses JSON format and is written atomically via `File.WriteAllText` after every register/unregister operation. If the PID file cannot be written (e.g., permissions), a warning is logged but execution continues — the tracker degrades gracefully.

---

## 9. `RUN SCRIPT` Nesting

`RUN SCRIPT` is handled at the **engine** layer (`RunScriptStatementHandler`) rather than the Orchestrator. It runs the sub-script **in-process**, within the same `Evaluator` context, using a new variable scope pushed onto the scope stack.

```
RUN SCRIPT 'C:\ETL\Transforms\normalize.etlsql' WITH (@region = @region_var);

1. Resolve @region_var value in calling scope
2. Lex + Parse normalize.etlsql
3. context.PushScope(localVars, metadata)
4. context.Evaluate(subscript)         ← recursive evaluator call
5. On completion:
     a. Map back parameter variables by identifier reference
     b. Map back any variables declared DECLARE @x ... OUTPUT
     c. context.PopScope()
```

**Recursion limit:** The evaluator enforces `MaxRecursiveDepth` (default 5). Scripts that call themselves (directly or transitively) beyond this depth receive a `SecurityException`. The `SET ALLOW_RECURSIVE_LAYERS = n` statement overrides this for approved safe-zone scripts.

**Scope isolation:** Variables declared inside the sub-script (without explicit `OUTPUT`) are invisible to the caller after it returns. Only parameters and explicitly `OUTPUT`-marked variables are returned.

### 9.1 Published Bundle VFS

`RUN SCRIPT` also resolves Orchestrator virtual paths:

```sql
RUN SCRIPT 'orch://finance-load@3/main.etlsql';
```

The VFS is backed by SQLite lockbox tables:

| Table | Purpose |
|---|---|
| `BundleVersions` | One row per immutable bundle version, including entry path, content hash, publish metadata, and encryption metadata |
| `BundleFiles` | Script/config file content for each bundle version, keyed by normalized virtual path |
| `BundleDependencies` | Literal `RUN SCRIPT` dependency edges discovered during publish |

Relative `RUN SCRIPT` calls inside an `orch://` script resolve within the same bundle version. Unversioned `orch://bundle/path` resolves to the latest version for manual runs. `CREATE JOB` and `ALTER JOB` pin unversioned paths to the current latest version before storing the job definition.

Dynamic `RUN SCRIPT` expressions cannot be published because the dependency graph cannot be sealed. They fail during `PUBLISH BUNDLE` or `VALIDATE BUNDLE` and must use live file mode.

Publish-time passwords are used only to unwrap existing `ENC:` values. Published copies remove `USE PASSWORD` statements and store secrets re-encrypted for the Orchestrator lockbox.

---

## 10. `PARALLEL` Block Scheduling

`PARALLEL` is also handled at the **engine** layer (`ParallelStatementHandler`). Each branch gets a **forked** execution context and runs as a concurrent `Task`:

```
PARALLEL [ WITH (CONCURRENCY = N) ]
BEGIN
    SELECT ...;       -- branch 1
    INSERT INTO ...;  -- branch 2
    RUN SCRIPT ...;   -- branch 3
END;

1. ConcurrencyLimit = N (or all branches if N=0)
2. SemaphoreSlim(ConcurrencyLimit) created for this block
3. For each statement S in body:
     a. await semaphore.WaitAsync()
     b. fork = context.Fork()   ← isolated copy of variables, #temp refs
     c. Task: fork.EvaluateStatement(S) → fork
     d. semaphore.Release()
4. await Task.WhenAll(all tasks)
5. for each fork in completion order: context.Merge(fork)
```

**Memory isolation:** Each fork gets its own copy of the variable dictionary and a reference-copy of shared `#temp` tables. Writes to shared `#temp` tables inside a `PARALLEL` branch are **not thread-safe** by default — the script author is responsible for ensuring no two branches write to the same `#temp` table simultaneously, or for using a `CONCURRENCY = 1` guard.

**Result merge:** After `Task.WhenAll`, forks are merged back into the parent context sequentially. Row counts, messages, and `ExecutionTree` nodes from each fork are appended to the parent in task-completion order.

---

## 11. Configuration Reference

All Orchestrator configuration is bound from `appsettings.json` in the host application:

```json
{
  "Jobs": {
    "MaxConcurrentJobs": 4,
    "ExecutablePath": "",
    "TimeoutSeconds": 3600,
    "UseProcessSpawning": false
  },
  "JobStore": {
    "DatabasePath": "etlsql.db"
  }
}
```

| Key | Default | Description |
|---|---|---|
| `Jobs:MaxConcurrentJobs` | `0` (auto) | Semaphore cap. `0` = `max(1, CPUCount/2)` |
| `Jobs:ExecutablePath` | `""` (auto-discover) | Full path to `ETL-SQL.exe` for `ProcessJobExecutor` |
| `Jobs:TimeoutSeconds` | `3600` | Per-job timeout before `Process.Kill`. `0` = unlimited |
| `Jobs:UseProcessSpawning` | `false` | `true` = use `ProcessJobExecutor`; `false` = use `ScriptExecutorAdapter` |
| `JobStore:DatabasePath` | `etlsql.db` | Path to the SQLite database file |

---

## 12. Troubleshooting Guide

### 12.1 A scheduled job is not firing

**Check 1:** Is `IsEnabled = 1` in the `Jobs` table? `DROP JOB` sets it to `0`, not `DELETE`.

**Check 2:** Is `NextRun` set to a later scheduled time? Query `SELECT Name, NextRun, LastRun FROM Jobs` directly against `etlsql.db` to inspect.

**Check 3:** Is the `SchedulerService` started? It must be called explicitly via `SchedulerService.Start()` at application boot. The DI container does not start it automatically.

**Check 4:** Is the scheduler loop throwing an exception? Check `SchedulerService` logs at `Error` level — unhandled exceptions in `RunAsync` are caught and logged, but the loop continues.

### 12.2 Job history shows `FAILURE` with no error message

**Cause:** `store.LogJobEndAsync` was called but `errorMessage` was `null` — the job threw an unhandled exception that was not surfaced to the result.

**Fix:** Check `ProcessJobExecutor` stderr output in application logs. If using in-process mode, check `ExecutionSession` logs at `Error` level for the failing session.

### 12.3 Orchestrator restart kills running jobs

**Cause:** `ChildProcessTracker.CleanupOrphans()` is killing processes from a previous run. If a job was still running when the Orchestrator was stopped, its PID survives in `child-pids.json` and is cleaned up on restart.

**Fix (operational):** Use `WAITFOR DELAY` within the job script to ensure short, bounded execution windows. For long-running jobs, consider breaking them into smaller scheduled segments.

**Fix (architectural):** Implement graceful shutdown — call `ChildProcessTracker.Unregister` for all active PIDs before the Orchestrator process exits, so they are not treated as orphans on the next start.

### 12.4 `RUN SCRIPT` triggers a "Maximum recursion depth exceeded" error

**Cause:** A chain of `RUN SCRIPT` calls has exceeded 5 levels of nesting.

**Fix:** Use `SET ALLOW_RECURSIVE_LAYERS = n;` in the script if the recursion is intentional and the script is stored in an approved safe zone. Otherwise, refactor the script into a flat structure using `#temp` tables to pass data between logical steps.

### 12.5 `PARALLEL` branches produce non-deterministic `#temp` table results

**Cause:** Two branches are writing to the same `#temp` table concurrently. The `Fork()` mechanism shares `#temp` table references, not copies.

**Fix:** Give each parallel branch its own uniquely-named `#temp` table. Merge results after the `PARALLEL` block using a `SELECT ... UNION ALL ...` or `INSERT INTO #merged SELECT * FROM #branch1; INSERT INTO #merged SELECT * FROM #branch2;` pattern.

---

*For the engine internals (Evaluator, Lexer, Parser, AST), see [Engine.md](Engine.md).*
*For connector implementation details, see [Connectors.md](Connectors.md).*
*For the language scheduling syntax (`CREATE JOB`, `RUN SCRIPT`, `PARALLEL`), see [Orchestrator Jobs](../reference/orchestrator-jobs/README.md) and [Control Flow](../reference/control-flow/README.md).*

---

## 13. Related Subsystem Architecture

For detailed information about adjacent subsystems, refer to the following architecture references:
- **Portal:** [Portal.md](Portal.md) explains REST APIs, authentication policies, and portal-proxied scheduler endpoints.
- **Reporting Engine:** [Reporting.md](Reporting.md) covers visual manifest structures and report file generation layers.
- **Portal UI & Designer:** [PortalUI.md](PortalUI.md) documents visual script editing, designer parsing, and DAG graph modeling.
