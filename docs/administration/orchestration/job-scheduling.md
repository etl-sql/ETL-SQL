# Job Scheduling

## 3. Job Scheduling

Jobs are scheduled from within your `.etlsql` scripts using the `CREATE JOB` statement. Once registered, they are stored in a SQLite database and executed automatically by the background scheduler — no cron job or Windows Task Scheduler entry is required.

### 3.0 Live Files vs Published Bundles

Orchestrator jobs support two script management models:

| Model | Syntax | Behavior |
|---|---|---|
| Live file | `RUN SCRIPT 'C:\ETL\main.etlsql'` | Reads the file from disk each time the job runs. File edits affect the next run. Use this for development or dynamic script dispatch. |
| Published bundle | `RUN SCRIPT 'orch://finance-load@3/main.etlsql'` | Runs an immutable script version stored in the Orchestrator lockbox. Use this for production jobs that must not change until republished. |

Unversioned published paths such as `orch://finance-load/main.etlsql` resolve to the latest published version for manual runs. When used in `CREATE JOB` or `ALTER JOB`, the Orchestrator resolves the latest version once and stores the pinned path, for example `orch://finance-load@3/main.etlsql`.

### 3.0.1 Publishing Scripts

```sql
PUBLISH BUNDLE 'finance-load'
FROM 'C:\ETL\finance'
ENTRY 'main.etlsql'
WITH (PASSWORD = 'publish-password', ENCRYPT = MACHINE);
```

`PUBLISH BUNDLE` stores an immutable bundle version. When `FROM` points at a directory, every `.etlsql` and `.rptsql` file under that directory is included, and literal relative `RUN SCRIPT 'child.etlsql'` dependencies are validated recursively. When `FROM` points at a single file, the bundle contains that entry file and its literal dependency closure. If the bundle content is unchanged from the latest version, the existing version is reused. If any file changed, the version increments.

Dynamic paths cannot be published:

```sql
RUN SCRIPT @nextScript;                 -- publish-time failure
RUN SCRIPT @folder + '\load.etlsql';    -- publish-time failure
```

Use live file mode for scripts that intentionally choose sub-scripts at runtime.

### 3.0.2 Passwords And Secrets

Publish-time passwords unlock existing `ENC:` values. Published copies are stored without `USE PASSWORD` statements, and secrets are re-encrypted for the Orchestrator lockbox. Source files are not modified.

`USE PASSWORD = 'literal'` is accepted for local/testing convenience, but it stores the password in source. Prefer `USE PASSWORD PROMPT` interactively, or provide the password on the publish command. Published scheduled jobs must not rely on runtime password prompts.

File encryption/decryption operations in published scripts must use explicit encrypted passwords or key references. They cannot rely on an implicit session password fallback.

### 3.0.3 Export And Recovery

```sql
EXPORT SCRIPT 'orch://finance-load@3/main.etlsql'
TO 'C:\Recovered\finance-load';
```

`EXPORT SCRIPT` recovers the bundled script files and relative folder structure. It does not decrypt or reveal secrets; recovered scripts may require secrets to be re-entered before use.

### 3.0.4 Bundle Inspection

```sql
SHOW PUBLISHED BUNDLES;
SHOW BUNDLE VERSIONS 'finance-load';
SHOW BUNDLE FILES 'finance-load' VERSION 3;
SHOW BUNDLE DEPENDENCIES 'finance-load' VERSION 3;
VALIDATE BUNDLE 'finance-load' FROM 'C:\ETL\finance' ENTRY 'main.etlsql';
```

### 3.1 `CREATE JOB` — Schedule a Job

```sql
-- Run every 30 minutes
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'C:\ETL\Scripts\cleanup.etlsql';

-- Run every hour
CREATE JOB HourlySync ON SCHEDULE EVERY 1 HOURS AS
BEGIN
    INSERT INTO dest.dbo.Events SELECT * FROM #events;
    PRINT 'Sync complete.';
END;

-- Run once daily at 2:00 AM with retries
CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00'
WITH (MAX_RETRIES = 3, RETRY_DELAY = 30)
AS
BEGIN
    INSERT INTO archive.dbo.Logs
    SELECT * FROM prod.dbo.Logs
    WHERE log_date < DATEADD(DAY, -30, GETDATE());

    DELETE FROM prod.dbo.Logs
    WHERE log_date < DATEADD(DAY, -30, GETDATE());

    PRINT 'Archive complete.';
END;
```

**Syntax:**
```
CREATE JOB <name> ON SCHEDULE EVERY <n> SECONDS|MINUTES|HOURS|DAYS [AT 'HH:MM']
[WITH (MAX_RETRIES = <n>, RETRY_DELAY = <seconds>)]
AS
    <single_statement>;

-- or with a block:
CREATE JOB <name> ON SCHEDULE EVERY <n> SECONDS|MINUTES|HOURS|DAYS [AT 'HH:MM']
[WITH (MAX_RETRIES = <n>, RETRY_DELAY = <seconds>)]
AS
BEGIN
    <statements>
END;
```

**Schedule units:**

| Unit | Example |
|------|---------|
| `SECONDS` | `EVERY 30 SECONDS` |
| `MINUTES` | `EVERY 15 MINUTES` |
| `HOURS` | `EVERY 4 HOURS` |
| `DAYS` | `EVERY 1 DAY AT '22:00'` — use `AT` to pin a wall-clock time |

> [!TIP]
> When using `EVERY 1 DAY AT '02:00'`, the job fires at 2:00 AM regardless of when the previous run ended. If the engine is restarted late, the next run is scheduled to the next 2:00 AM occurrence.

### 3.2 Retry Policies & Resilience

Jobs can be configured with a retry policy to handle transient failures (e.g., network glitches, database timeouts).

| Option | Default | Description |
|--------|---------|-------------|
| `MAX_RETRIES` | `0` | Number of times to retry a failed job attempt. |
| `RETRY_DELAY` | `30` | Initial delay in seconds between retries. |

**Exponential Backoff:**
The scheduler employs an exponential backoff strategy. The delay doubles with each subsequent attempt: $delay \times 2^{(attempt-1)}$, capped at 1 hour.

**Session Persistence:**
Retries automatically preserve the `SessionId` from the first attempt. This ensures that any persisted state (connections, variables, `#temp` tables) remains available to the retried script if the environment is configured for session persistence.

### 3.3 `SHOW JOBS` — List Registered Jobs

Displays all registered jobs with their schedule, last run time, and next scheduled run.

```sql
SHOW JOBS;

-- Or capture into a temp table for further processing
SHOW JOBS INTO #job_list;
SELECT * FROM #job_list WHERE IsEnabled = 1;
```

**Result columns:** `Name`, `Interval`, `Unit`, `AtTime`, `LastRun`, `NextRun`, `IsEnabled`

### 3.4 `SHOW JOB HISTORY` — View Execution History

Returns the execution log for all jobs or a specific job.

```sql
-- All job history (last 100 entries)
SHOW JOB HISTORY;

-- History for a specific job
SHOW JOB HISTORY NightlyArchive;

-- Capture for analysis
SHOW JOB HISTORY INTO #history;
SELECT
    JobName,
    StartTime,
    EndTime,
    Status,
    RowsProcessed,
    ErrorMessage
FROM #history
WHERE Status = 'FAILURE'
ORDER BY StartTime DESC;
```

**Result columns:** `Id`, `JobName`, `StartTime`, `EndTime`, `Status`, `ErrorMessage`, `RowsProcessed`

Each run also records its data-quality outcomes, so quality can be trended alongside volume and
duration:

| Column | Contents |
| :--- | :--- |
| `RowsQuarantined` | Rows removed from output by an `@expect` … `QUARANTINE` action. |
| `RowsWarned` | Rows that failed a `WARN` rule but still reached the target. |
| `DataQualityFailures` | Compact per-rule failure counts (`column:rule=count;…`). Counts only — sample values are never persisted here. |

These columns are added by an additive migration on both the SQLite and PostgreSQL providers, so an
existing history table is upgraded in place and pre-existing rows read back with zeroes. They are
also what `ASSERT JOB … WITHIN … OF HISTORICAL` reads to build its baseline — see
[ASSERT JOB](../../reference/statements/session-control/assert-job.md).

**Status values:** `RUNNING` (in-flight), `SUCCESS`, `FAILURE`, `BLOCKED` (script-hash mismatch),
`QUARANTINED`, and `INTERRUPTED` (a job whose completion was never recorded — the orchestrator
restarted or the job was killed; reconciled from a stale `RUNNING` row). A failure digest should treat
everything except `SUCCESS` and `RUNNING` as a problem.

### 3.5 `SHOW HOST METRICS` — Host Utilization (Capacity Planning)

Returns the host-utilization time series — per node, the last 24 hours of memory-load %, CPU %, and
free disk on the state and spill volumes. This is the signal for *"am I outgrowing this server"*,
distinct from the per-job cost in `SHOW JOB HISTORY`. Samples are recorded on the node heartbeat and
pruned per `Orchestrator:HostMetricsRetentionDays` (default 14).

```sql
-- All nodes, or filter to one node id
SHOW HOST METRICS;
SHOW HOST METRICS 'app-server-01:1234:ab...';

-- Capacity check: lowest free disk and peak memory per node in the last 24h
SHOW HOST METRICS INTO #hm;
SELECT NodeId,
       MIN(StateDiskFreeMB) AS MinStateFreeMB,
       MIN(SpillDiskFreeMB) AS MinSpillFreeMB,
       MAX(MemoryLoadPercent) AS PeakMemPct
FROM #hm
GROUP BY NodeId;
```

**Result columns:** `NodeId`, `CapturedAt`, `MemoryLoadPercent`, `ProcessCpuPercent`, `HostCpuPercent`
(null until a whole-host CPU probe is enabled), `StateDiskFreeMB`, `SpillDiskFreeMB`

### 3.4 `DROP JOB` — Unschedule a Job

Removes a job definition and its full history from the store.

```sql
DROP JOB IF EXISTS CleanupJob;
DROP JOB NightlyArchive;
```

> [!CAUTION]
> `DROP JOB` permanently deletes the job and all its history entries. There is no undo. If you just want to pause a job temporarily, update it through the Portal or call `PUT /api/scheduled-jobs/{name}` with `IsEnabled = false`.

### 3.5 Cancelling a Running Job

To request cancellation of a currently-executing scheduled job, use the Orchestrator REST API:

```bash
curl -X POST http://localhost:5001/api/scheduled-jobs/{name}/kill \
  -H "X-Orchestrator-Key: your-shared-secret"
```

For ad-hoc jobs submitted directly to `POST /jobs`, cancel by job id. Like submission, the ad-hoc job routes require the `X-Orchestrator-Key` header when an API key is configured:

```bash
curl -X DELETE http://localhost:5001/jobs/{id} \
  -H "X-Orchestrator-Key: your-shared-secret"
```

From within a script or TUI session, the in-engine scheduler will also automatically stop a job's execution if its `CancellationToken` is triggered (e.g. via `Ctrl+C` or process shutdown).

### 3.6 Paging Scheduled Jobs and History

The management APIs bound catalog responses so large installations do not materialize every job or
history row in one request. Job pages are ordered by name and accept `limit` (1–1,000, default 100)
and `offset`. History endpoints accept `limit` (1–1,000).

```text
GET /api/scheduled-jobs?limit=100&offset=200
GET /api/scheduled-jobs/NightlyArchive/history?limit=100
GET /api/history?jobName=NightlyArchive&limit=100
```

Scheduler due-job reads are not paged because every due job must be considered in a scheduling pass;
they use the `(IsEnabled, NextRun)` index. Portal completion polling uses bounded 1,000-row,
completion-time pages internally.

---

