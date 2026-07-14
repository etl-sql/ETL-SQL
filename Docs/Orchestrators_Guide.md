# ETL-SQL Orchestrator's Guide

**Audience:** Operators, data engineers, and pipeline administrators who need to schedule, run, and monitor ETL-SQL jobs from the command line.

---

## 1. Overview

The ETL-SQL engine ships as a single executable (`ETL-SQL.exe` on Windows, `ETL-SQL` on Linux/macOS) that works in three modes:

| Mode | Purpose |
|------|---------|
| **CLI / Headless** | Run a `.etlsql` script from a shell, CI/CD pipeline, or Task Scheduler. |
| **Terminal IDE (TUI)** | Interactive editor with live execution tree, results panel, and autocomplete. |
| **Background Scheduler** | The scheduler starts automatically at launch and continuously polls scheduled jobs. |

All three modes share the same background scheduler. Any `CREATE JOB` statement registered in a script is persisted and will fire at its next scheduled time even when the Terminal IDE is not open.

---

## 2. CLI Command Reference

```
ETL-SQL [command] [arguments] [options]
```

Running `ETL-SQL` with no arguments (or `--help`) displays the command table.

### 2.1 `run` — Execute a Script

Runs an `.etlsql` script file and exits.

```
ETL-SQL run <script> [options]
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `<script>` | Path to the `.etlsql` script file to execute |

**Options:**

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--batch-size` | `-b` | `10000` | Number of rows per in-memory processing batch |
| `--perf` | `-p` | off | Print performance metrics (Lexer/Parser/Execution ms, RAM, rows/s) after execution |
| `--verbose` | `-v` | off | Print detailed statement-level execution tracking |
| `--log [path]` | `-l` | off | Enable log file. Defaults to `logs/scripts/`. Override with a path. |
| `--silent` | `-s` | off | Suppress all console output |
| `--preview [n]` | `-pr` | off | Preview top N rows of the result set in the console (`*` for all) |
| `--json` | | off | Emit all output as structured JSON (used by the VS Code extension) |
| `--page` | `-pa` | off | Pause between multiple result sets (interactive pager) |
| `--session <id>` | | none | Enable session persistence. Connections and variables survive between runs. |
| `--var @Name=Value` | `-d` | none | Inject a variable into the script. Repeatable. |
| `--progress` | `-g` | off | Display a live graphical execution tree in the console |

**Examples:**

```bash
# Simplest run
ETL-SQL run nightly_load.etlsql

# With perf metrics and logging
ETL-SQL run nightly_load.etlsql --perf --log C:\Logs\etlsql\

# Inject runtime parameters
ETL-SQL run monthly_report.etlsql --var @env=PROD --var @month=2026-03

# Headless with JSON output for automation
ETL-SQL run nightly_load.etlsql --json --silent

# Persistent session — connections survive between runs
ETL-SQL run setup_connections.etlsql --session prod-session
ETL-SQL run nightly_load.etlsql --session prod-session

# Live progress tree in the terminal
ETL-SQL run heavy_transform.etlsql --progress --perf
```

### 2.2 `ui edit` — Open the Terminal IDE

Opens the full Terminal IDE (windowed TUI editor) with optional pre-loaded file.

```
ETL-SQL ui edit [file] [options]
```

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `[file]` | | none | Optional `.etlsql` file to pre-load into the editor |
| `--batch-size` | `-b` | `10000` | Batch size for executions started from the IDE |
| `--verbose` | `-v` | off | Verbose mode for executions started from the IDE |
| `--session <id>` | | none | Session ID — connections persist across F5 runs |

```bash
# Open the IDE with a file pre-loaded
ETL-SQL ui edit nightly_load.etlsql

# Open the IDE with a persistent session
ETL-SQL ui edit --session dev-workspace
```

### 2.3 `ui repl` — JSON REPL Protocol

Starts the JSON-based REPL protocol used by the VS Code extension. Not intended for direct interactive use.

```
ETL-SQL ui repl [options]
```

### 2.4 `encrypt` — Encrypt a Connection String

Encrypts a plaintext value (typically a connection string or password) so it can be stored safely in a script using the `ENC:` prefix.

```
ETL-SQL encrypt <value> --pass <master-password>
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `<value>` | The plaintext connection string or password to encrypt |

**Options:**

| Option | Description |
|--------|-------------|
| `--pass <password>` | The master password used for AES-256 encryption |

**Example:**

```bash
# Encrypt a connection string
ETL-SQL encrypt "Server=prod-sql;Database=DW;User Id=sa;Password=S3cr3t!" --pass MyMasterKey

# Output:
# Encrypted: ENC:U2FsdGVkX1+...

# Use in a script:
# CREATE CONNECTION prod AS MSSQL('ENC:U2FsdGVkX1+...', TRUSTED_CONNECTION=FALSE);
```

> [!IMPORTANT]
> The master password must be the same each time you run scripts referencing `ENC:` strings. Pass it at runtime with `--pass MyMasterKey` or set `USE PASSWORD = '...';` at the top of your script.

### 2.5 `session clear` — Clear Session State

Removes persisted session state (connections, variables) for the given session ID.

```
ETL-SQL session clear <id>
```

```bash
ETL-SQL session clear dev-workspace
```

### 2.6 `generate` — Generate Mock Data

Generates a large test dataset for performance validation.

```
ETL-SQL generate [--estimate <rows>]
```

### 2.7 `gen-script` — Compile Spec JSON to Script

Compiles an intermediate JSON specification contract into a validated `.etlsql` starter script. This is intended to save setup time after an LLM or developer extracts a vendor data specification into JSON; it does not replace human review or the source extraction query.

```
ETL-SQL gen-script --schema <path-to-json> --output <path-to-etlsql>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--schema` | `-s` | Path to the input JSON schema specification file |
| `--output` | `-o` | Destination path for the compiled ETL-SQL script |

**Example:**
```bash
ETL-SQL gen-script --schema ./specs/customer_feed.json --output ./scripts/load_customers.etlsql
```

Generated scripts include schema gates, casting, lineage tags, AI review/evidence comments when present, validation issue summaries, and optional quarantine scaffolding. Review the JSON, complete the generated `#staging` extraction block, and test with real vendor files before production use. See `Docs/Reference/Spec_Driven_Development.md` and Cookbook recipe 25 for the full workflow.

### 2.8 `extract-spec` — Trim Schema Pages from Large PDF

Uses heuristic analysis to extract likely data dictionary / schema pages from large vendor PDF specifications, removing administrative fluff before LLM review.

```
ETL-SQL extract-spec --input <path-to-large-pdf> --output <path-to-trimmed-pdf>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--input` | `-i` | Path to the input large PDF specification file |
| `--output` | `-o` | Destination path for the extracted trimmed PDF file |

**Example:**
```bash
ETL-SQL extract-spec --input ./specs/vendor_api_spec.pdf --output ./specs/trimmed_schema_spec.pdf
```

### 2.9 Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Script completed successfully |
| `1` | Parse error, lint error, or runtime exception |

Exit codes are suitable for use in CI/CD pipeline gating.

---

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
> `DROP JOB` permanently deletes the job and all its history entries. There is no undo. If you just want to pause a job temporarily, update it through the Report Portal or call `PUT /api/scheduled-jobs/{name}` with `IsEnabled = false`.

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

## 4. Session Persistence

Sessions let connections and variables defined in one run survive into the next. This is most useful when you split your pipeline across multiple scripts or F5 runs.

### 4.1 How sessions work

When you pass `--session <id>`:
1. At **start** of a run: the engine loads the saved state (connections + variables) for `<id>` into the evaluator.
2. At **end** of a run: the engine saves the final evaluator state back to `<id>`.

Session state is stored in an encrypted file on disk (keyed to your `--pass` password if provided).

### 4.2 Usage pattern

```bash
# Step 1: Set up long-lived connections once
ETL-SQL run 01_connect.etlsql --session nightly --pass MyKey

# Step 2: Run the extract (connections from step 1 still live)
ETL-SQL run 02_extract.etlsql --session nightly --pass MyKey

# Step 3: Load (connections still live)
ETL-SQL run 03_load.etlsql --session nightly --pass MyKey

# Cleanup: reset session to force fresh connections next time
ETL-SQL session clear nightly
```

### 4.3 Stale session cleanup

Sessions that have not been used for 7 days are automatically removed on the next `run` invocation. You can clear one manually at any time with `ETL-SQL session clear <id>`.

---

## 5. Variable Injection

You can pass variables from the CLI into your script using `--var`:

```bash
ETL-SQL run monthly_report.etlsql \
  --var @env=PROD \
  --var @startDate=2026-01-01 \
  --var @endDate=2026-03-31
```

Inside the script, use these as normal `@variables`:

```sql
DECLARE @env VARCHAR(10);         -- declared but value comes from CLI
DECLARE @startDate DATE;
DECLARE @endDate DATE;

SELECT * FROM prod.sales
WHERE region = @env
  AND sale_date BETWEEN @startDate AND @endDate;
```

> [!NOTE]
> CLI variables are treated as **input parameters** — they are injected before script execution begins. They do not need to be declared with `DECLARE` (the engine will accept them even without an explicit `DECLARE`), but declaring them is good practice for IDE autocomplete.

**Type coercion:** The CLI automatically converts the string value to the most appropriate type (int, double, bool, DateTime, or string).

---

## 6. Logging

### 6.1 Enable log files

```bash
# Log to the default directory (logs/scripts/)
ETL-SQL run nightly_load.etlsql --log

# Log to a specific directory
ETL-SQL run nightly_load.etlsql --log C:\ETL\Logs\

# Log to a specific file
ETL-SQL run nightly_load.etlsql --log C:\ETL\Logs\nightly-$(date +%Y%m%d).log
```

### 6.2 Log configuration (`appsettings.json`)

Log retention and size limits are controlled in `appsettings.json` next to the executable:

```json
{
  "Logging": {
    "ScriptLog": {
      "Directory": "logs/scripts",
      "DefaultRetentionDays": 30,
      "FileSizeLimitMb": 10
    }
  }
}
```

Log files are named after the script file with a date suffix (e.g., `nightly_load_20260413.log`).

---

## 7. Performance Tuning

### 7.1 Batch size

The `--batch-size` option controls how many rows are buffered in memory at one time. The default of 10,000 is suitable for most workloads. Tune this based on available RAM and row width:

```bash
# Large, wide rows — reduce batch size to avoid memory pressure
ETL-SQL run big_transform.etlsql --batch-size 2000

# Narrow rows with fast I/O — increase for throughput
ETL-SQL run csv_import.etlsql --batch-size 50000
```

### 7.2 Performance metrics

Use `--perf` to get a post-execution breakdown:

```bash
ETL-SQL run nightly_load.etlsql --perf
```

Output includes:
- Lexer / Parser / Execution phase timings (ms)
- Total rows processed
- Throughput (rows/second)
- Approximate RAM peak (MB)
- Disk-spill volume if the aggregate engine overflowed

### 7.3 Per-statement profiling

Use `SET PROFILING ON` inside your script to capture timings at the individual statement level:

```sql
SET PROFILING ON;

SELECT * INTO #staging FROM src.Orders WHERE status = 'Open';
MERGE INTO dest.dbo.Orders AS T USING #staging AS S ON T.Id = S.Id ...;

SHOW PROFILE INTO #perf;
SELECT * FROM #perf ORDER BY DurationMs DESC;
```

---

## 8. CI/CD Integration

### 8.1 Shell / PowerShell

```powershell
# Run a script and check the exit code
& "C:\ETL\ETL-SQL.exe" run "C:\ETL\Scripts\nightly.etlsql" `
    --var @env=PROD `
    --log "C:\ETL\Logs\" `
    --silent

if ($LASTEXITCODE -ne 0) {
    Write-Error "ETL script failed with exit code $LASTEXITCODE"
    exit 1
}
```

### 8.2 GitHub Actions / Azure Pipelines

```yaml
- name: Run ETL pipeline
  run: |
    ./etl-sql run scripts/nightly_load.etlsql \
      --var @env=PROD \
      --log logs/ \
      --silent
  env:
    ETL_MASTER_PASSWORD: ${{ secrets.ETL_MASTER_PASSWORD }}
```

Use `--pass $ETL_MASTER_PASSWORD` if your scripts use `ENC:` strings:

```yaml
- name: Run ETL pipeline
  run: |
    ./etl-sql run scripts/nightly_load.etlsql \
      --pass "$ETL_MASTER_PASSWORD" \
      --silent
```

### 8.3 Windows Task Scheduler

For simple scheduled jobs without needing the in-engine scheduler:

1. **Program/script:** `C:\ETL\ETL-SQL.exe`
2. **Arguments:** `run "C:\ETL\Scripts\nightly.etlsql" --log "C:\ETL\Logs\" --silent`
3. **Set a trigger:** Daily at 02:00

For pipelines that need the **in-engine scheduler** (multiple inter-dependent recurring jobs), prefer using `CREATE JOB` statements and running the engine continuously as a Windows Service or daemon. See §9 below.

**Using `schtasks.exe` from the command line:**

```powershell
# Create a daily task at 02:00 AM
schtasks /Create /TN "ETL-SQL Nightly" /TR `
  "\"C:\ETL\ETL-SQL.exe\" run \"C:\ETL\Scripts\nightly.etlsql\" --silent --log \"C:\ETL\Logs\\\"" `
  /SC DAILY /ST 02:00 /RU SYSTEM /F

# Create an hourly task
schtasks /Create /TN "ETL-SQL Hourly Sync" /TR `
  "\"C:\ETL\ETL-SQL.exe\" run \"C:\ETL\Scripts\hourly_sync.etlsql\" --silent" `
  /SC HOURLY /RU SYSTEM /F

# Run a task immediately (for testing the schedule)
schtasks /Run /TN "ETL-SQL Nightly"

# View task status and last result
schtasks /Query /TN "ETL-SQL Nightly" /FO LIST /V

# Remove a task
schtasks /Delete /TN "ETL-SQL Nightly" /F
```

> [!NOTE]
> `/RU SYSTEM` runs the task as the Local System account. Replace with a specific service account (`/RU DOMAIN\svcETL /RP password`) if your scripts connect to network resources or need domain credentials.

### 8.4 Linux / macOS Cron Jobs

On Linux and macOS, use `crontab` to schedule ETL-SQL scripts as standard cron jobs.

**Cron expression format:**
```
┌───────────── minute (0–59)
│ ┌─────────── hour (0–23)
│ │ ┌───────── day of month (1–31)
│ │ │ ┌─────── month (1–12)
│ │ │ │ ┌───── day of week (0–7, 0 and 7 = Sunday)
│ │ │ │ │
* * * * *  command
```

**Edit your crontab:**
```bash
crontab -e
```

**Common schedule examples:**
```cron
# Daily at 2:00 AM
0 2 * * *    /opt/etlsql/etl-sql run /opt/etl/scripts/nightly.etlsql --silent --log /var/log/etlsql/

# Every 30 minutes
*/30 * * * * /opt/etlsql/etl-sql run /opt/etl/scripts/sync.etlsql --silent

# Every hour
0 * * * *    /opt/etlsql/etl-sql run /opt/etl/scripts/hourly.etlsql --silent

# Weekdays at 6:00 AM only (Monday–Friday)
0 6 * * 1-5  /opt/etlsql/etl-sql run /opt/etl/scripts/morning_load.etlsql --silent

# First day of every month at midnight
0 0 1 * *    /opt/etlsql/etl-sql run /opt/etl/scripts/monthly_archive.etlsql --silent --log /var/log/etlsql/
```

**With variable injection and error logging:**
```cron
# Inject parameters and capture stderr separately
0 2 * * * /opt/etlsql/etl-sql run /opt/etl/scripts/nightly.etlsql \
  --var @env=PROD \
  --silent \
  --log /var/log/etlsql/ \
  >> /var/log/etlsql/cron.log 2>&1
```

**With master password from environment:**
```cron
# Load secrets from a protected env file, then run
0 2 * * * . /etc/etlsql/secrets.env && \
  /opt/etlsql/etl-sql run /opt/etl/scripts/nightly.etlsql \
  --pass "$ETL_MASTER_PASSWORD" \
  --silent --log /var/log/etlsql/
```

> [!CAUTION]
> Never embed passwords directly in crontab files — they are readable by any user with `crontab -l` access. Use environment files (mode `600`, owned by the cron user) or a secrets manager.

**Check cron execution logs:**
```bash
# Systemd-based distros
journalctl -u cron --since "today"

# Traditional syslog
grep CRON /var/log/syslog | tail -50

# View your own crontab
crontab -l

# Remove your crontab entirely
crontab -r
```

### 8.5 Choosing the right scheduling approach

| Approach | Platform | Best for |
|----------|----------|----------|
| `CREATE JOB` (in-engine) | Both | Multiple inter-dependent recurring jobs, jobs that need ETL-SQL context, history tracking via `SHOW JOB HISTORY` |
| Windows Task Scheduler / `schtasks` | Windows | Single one-off scripts, OS-managed scheduling, no need to keep the engine running 24/7 |
| Linux crontab | Linux/macOS | Single scripts on a fixed schedule, minimal infrastructure, containerized environments |
| Windows Service (§9.1) + `CREATE JOB` | Windows | Production servers running `CREATE JOB` continuously with automatic restart |
| systemd service (§9.2) + `CREATE JOB` | Linux/macOS | Production servers running `CREATE JOB` continuously with automatic restart |

> [!TIP]
> **Rule of thumb:** If you have more than two or three recurring pipelines that share data or depend on each other, use `CREATE JOB` with the in-engine scheduler. For a single nightly script with no dependencies, OS-level scheduling (Task Scheduler or cron) is simpler and requires no long-running process.


---

## 9. VS Code Extension

ETL-SQL ships with a dedicated VS Code extension (`src/etl-sql-vscode/`) that enhances the development experience. The extension communicates with the engine via the JSON REPL protocol (`ETL-SQL ui repl`).

**Key features:**
- **Syntax highlighting** for `.etlsql` and `.rptsql` files
- **Inline LINT** — static analysis errors appear as squiggles as you type
- **Execution tree** — visual representation of the running pipeline
- **Variable sidebar** — live display of declared and runtime variable values
- **Report preview panel** — `CREATE PAGE` dashboards rendered inline for `.rptsql` files
- **Slicer interaction** — filter parameters can be changed in the sidebar without re-running the full script

**Starting the extension host:**
The extension auto-launches `ETL-SQL ui repl` in the background when you open an `.etlsql` or `.rptsql` file. For configuration, see the VS Code settings under `etlsql.*`.

---

## 10. Configuration & Deployment

Host-level settings, including security limits, dashboard ports, and background service deployment (NSSM/systemd), are now managed in the central **[Administrators Guide](Administrators_Guide.md)**.

Refer to that guide for:
- **`appsettings.json`** configuration keys.
- **Security Limits** (runaway protection).
- **Background Service** installation.
- **Resource Governance** (memory and disk spilling).


---

## 11. Resource Governance

To prevent Out-Of-Memory (OOM) errors and database connection exhaustion in multi-user environments, the Orchestrator employs a **Buffer Manager**.

### 11.1 Resource Queuing (FIFO)
When global limits for RAM (`MaxGlobalMemoryMB`) or Database Cursors (`MaxStreamingCursors`) are reached, new requests are placed in a **First-In, First-Out (FIFO)** queue. 

- **Graceful Wait**: The engine will block and wait for resources to become available.
- **Visual Feedback**: Every minute, the engine prints a status update to the console or session log: `Waiting for resources... (T-4 minutes remaining)`. This allows operators to differentiate between a hung process and a resource-constrained wait.
- **Timeout**: If the resource is not granted within the configured `ResourceWaitTimeoutSeconds` (default 10 minutes), the script fails with a `TimeoutException`.

### 11.2 Hysteresis (Memory Cooldown)
To prevent "resource thrashing" (where the engine constantly starts and immediately stalls tasks as tiny amounts of memory fluctuate), the Buffer Manager employs a **Hysteresis Threshold**.

Once the global memory limit is hit and the engine enters the "Exhausted" state:
1. All new memory requests are queued.
2. Queue processing is **suspended** until memory usage drops below a safe threshold.
3. Safe threshold = `MaxGlobalMemoryMB - HysteresisMemoryMB`.
4. This ensures that when the engine resumes, it has enough room to process at least a few full batches without immediately re-entering the exhausted state.

### 11.3 Policy Overrides
Users can bypass global resource governors using `SET` commands (e.g., `SET MAX_MEMORY = 4096`). 

> [!WARNING]
> **Accountability**: Any resource request that exceeds the global policy via a `SET` command is logged with a `[POLICY_OVERRIDE]` tag in the central AppLog. This allows administrators to trace system instability back to specific user-initiated overrides.

---

## 12. Troubleshooting

### The scheduler isn't firing my job

1. Check that the executable is running (`ETL-SQL ui repl` or as a service). The scheduler only runs while the process is live.
2. Query `SHOW JOBS;` — verify `IsEnabled = 1` and `NextRun` is in the past.
3. Check `logs/` for scheduler error entries at the `Error` level.
4. If using process spawning (`UseProcessSpawning = true`), verify `ExecutablePath` points to a valid executable.

### A scheduled job shows `FAILURE` with no error message

Run the job's script manually first to reproduce the error interactively:

```bash
ETL-SQL run C:\ETL\Scripts\nightly.etlsql --verbose --log
```

This surfaces the full error with line numbers. Fix the script, then let it be picked up by the scheduler on its next `NextRun`.

### `ENC:` strings fail to decrypt

The master password used to encrypt must match the one passed at runtime (`--pass` or `USE PASSWORD`). Passwords are case-sensitive. Re-encrypt with the correct password:

```bash
ETL-SQL encrypt "Server=prod;Database=DW;..." --pass CorrectPassword
```

### Session state is stale or corrupt

Clear the session and let it rebuild:

```bash
ETL-SQL session clear <session-id>
```

### Performance is slower than expected

1. Use `--perf` to identify which phase (Lex/Parse/Execute) takes the most time.
2. Use `SET PROFILING ON` + `SHOW PROFILE` inside the script to find slow statements.
3. Reduce `--batch-size` if you are hitting memory pressure (large rows); increase it for small rows with fast I/O.
4. For cross-database `INSERT INTO ... SELECT FROM` pipelines, ensure the source connection implements SQL pushdown (`IDatabaseSource` with `SupportsSqlPushdown = true`) to avoid row-by-row transfer.

---

## 13. Advanced Orchestration: DAGs (Directed Acyclic Graphs)

As your data ecosystem grows, you will inevitably need to orchestrate complex dependencies where scripts must run in a specific order, sometimes in parallel, and often gated by the appearance of external data.

### 13.1 Composition with `RUN SCRIPT`

ETL-SQL promotes **modular orchestration**. Instead of one 5,000-line script, break your pipeline into logical modules (Extract, Transform, Load) and coordinate them from a "Main" orchestrator script.

```sql
-- Main Orchestrator.etlsql
DECLARE @run_date DATE = GETDATE();

PRINT '--- Starting Nightly Pipeline ---';

RUN SCRIPT '01_Extract.etlsql' WITH (@date = @run_date);
RUN SCRIPT '02_Transform.etlsql' WITH (@date = @run_date);
RUN SCRIPT '03_Load.etlsql' WITH (@date = @run_date);

PRINT '--- Pipeline Complete ---';
```

### 13.2 Fan-out / Parallel Execution

When multiple independent scripts can run at once, use the `PARALLEL` block. This significantly reduces total wall-clock time by utilizing all available CPU cores and I/O bandwidth.

```sql
PRINT 'Starting parallel extracts...';

PARALLEL (4) -- Limit to 4 concurrent scripts
BEGIN
    RUN SCRIPT 'extract_erp.etlsql';
    RUN SCRIPT 'extract_crm.etlsql';
    RUN SCRIPT 'extract_logs.etlsql';
    RUN SCRIPT 'extract_legacy.etlsql';
END;

PRINT 'All extracts finished. Starting transformation...';
RUN SCRIPT 'global_transform.etlsql';
```

> [!NOTE]
> The engine automatically handles **Context Forking** inside `PARALLEL` blocks. Each script runs in its own isolated environment, and variables/connections are merged back into the main thread in the order they were submitted.

### 13.3 Dependency Gating (WAIT UNTIL) Gating

Often, a pipeline should not start until an external file arrives (e.g., from an SFTP source or an upstream process). Use `WAITFOR (condition)` to create dynamic gates.

```sql
PRINT 'Waiting for daily_source.csv...';

-- Polls every 200ms until the file exists
WAITFOR (FILE_EXISTS('C:\DropZone\daily_source.csv'));

PRINT 'File arrived. Beginning ingestion.';
RUN SCRIPT 'ingest_file.etlsql';
```

### 13.4 Conditional Branching

You can use standard `IF` logic to handle optional execution paths based on data state or environment.

```sql
-- Only run the archive job on the first day of the month
IF DAY(GETDATE()) = 1
BEGIN
    PRINT 'Detected first of month. Running archive...';
    RUN SCRIPT 'monthly_archive.etlsql';
END
ELSE
BEGIN
    PRINT 'Skipping archive (not 1st of month).';
END
```

### 13.5 Visualizing the DAG

When running complex nested scripts, use the `--progress` flag in the CLI:

```bash
ETL-SQL run orchestrator.etlsql --progress
```

The console will render a live, dynamic **Execution Tree** showing which scripts are currently running, which are queued in `PARALLEL` blocks, and their individual progress bars.

---

---

## 14. Orchestrator Management Portal

The **Orchestrator Management Portal** is a browser-based dashboard embedded in the ETL-SQL Report Portal that gives administrators full visibility and control over scheduled jobs without needing the CLI or a SQLite viewer.

### 14.1 Prerequisites

The management portal is hosted inside the Report Portal (`ETL-SQL-Portal`). The Orchestrator Service (`ETL-SQL-Service`) must be running and reachable from the machine that runs the portal. The two services communicate over HTTP using a shared API key.

### 14.2 Enabling the API Key

By default the Orchestrator's management endpoints are open (no authentication). For production, set a shared secret on both sides.

**On the Orchestrator Service** (`appsettings.json` or environment variable):

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "/opt/etl/scripts"
  }
}
```

Or via environment variable:
```
Orchestrator__ApiKey=your-shared-secret
Orchestrator__ScriptRoot=/opt/etl/scripts
```

**On the Report Portal** — two options:

*Option A — `appsettings.json` / environment variable (applied at startup):*
```json
{
  "Portal": {
    "Orchestrator": {
      "ApiUrl": "http://orchestrator-host:5001",
      "ApiKey": "your-shared-secret"
    }
  }
}
```
Or:
```
Portal__Orchestrator__ApiUrl=http://orchestrator-host:5001
Portal__Orchestrator__ApiKey=your-shared-secret
```

*Option B — Admin UI (applied immediately, no restart needed):*
Log in as Admin, navigate to **Admin → Settings → Orchestrator Connection**, enter the URL and API key, and click **Save**. Settings are written to a `portal-orchestrator.json` sidecar file and take effect on the very next request. UI-saved settings take precedence over environment variables.

### 14.3 Script Root

The portal's **Create Job** modal lets users pick a script file from a browser rather than typing a raw path. The Orchestrator Service exposes the file browser under a configured root directory:

```json
{
  "Orchestrator": {
    "ScriptRoot": "C:\\ETL\\Scripts"
  }
}
```

If `ScriptRoot` is not set it defaults to the Orchestrator's working directory. The file browser only surfaces `.etlsql` files and prevents path traversal outside the root.

### 14.4 Granting Portal Access

Two roles can access the Orchestrator tab in the portal:

| Role | Access |
| :--- | :--- |
| **Admin** | Full access — Orchestrator tab is always visible |
| **OrchestratorManager** | Orchestrator tab only — cannot access the Admin panel |

Assign the `OrchestratorManager` role to operations staff who need to manage jobs but should not be able to create users or manage reports. See the [Report Portal Administrator's Guide](./ReportPortal_Administrators_Guide.md#orchestrator-manager-role) for role assignment instructions.

### 14.5 Dashboard Features

Navigate to the Orchestrator tab in the portal after logging in with an eligible role.

**Stats bar** — five chips that auto-refresh every 10 seconds: service status (Online/Offline badge), Active Jobs, Queued, Completed Today, Failed Today.

**24-hour Gantt chart** — a timeline from 00:00 to 23:59 showing each job as a horizontal bar at its scheduled firing time, sized by historical average duration. Blue bars are enabled jobs; grey bars are disabled. Click any bar to open the job detail panel.

**Jobs table** — all jobs including disabled ones (disabled rows are visually dimmed). Columns: Name, Schedule, Status, Last Run, Next Run, Actions. Actions per row:

| Action | What it does |
| :--- | :--- |
| **Run** | Triggers an immediate out-of-schedule execution |
| **Enable / Disable** | Toggles `IsEnabled` — the job stays in the database and remains visible when disabled |
| **Kill** | Cancels a currently-running execution (requires the job to have a `RUNNING` history entry) |
| **Delete** | Permanently removes the job and all its history (equivalent to `DROP JOB`) |

**Job detail panel** — slides in from the right when you click a job or Gantt bar. Shows:
- Schedule definition and next fire time
- Script content as of the last save (read-only, monospace)
- Duration trend sparkline (last 30 runs)
- History table: last 20 executions with Status, Start Time, Duration, Rows Processed, Peak RAM, CPU Time, and any error message

**Create Job modal** — opens via the **New Job** button. Fields:
- Job name
- Script — dropdown of files from the Orchestrator's script root, with a manual path fallback
- Schedule: Every N `SECONDS / MINUTES / HOURS / DAYS`, optional `AT HH:MM` for day-level jobs
- Max Retries and Retry Delay
- Hash Policy (`Warn`, `Block`, or `Off`)

> [!NOTE]
> Jobs created through the portal store the full script content in the database at creation time. If the `.etlsql` file on disk is edited later, the database copy is not automatically updated. Re-save the job through the portal (or via `CREATE JOB … AS …` in a script) to pick up changes.

### 14.6 Service Control

When the Orchestrator is **online**, two service-control buttons appear next to the Online chip:

- **Stop** — sends `POST /management/stop` to the Orchestrator, which calls `IHostApplicationLifetime.StopApplication()`. If the Orchestrator is registered as a Windows Service or systemd unit, the OS supervisor restarts it automatically. The portal polls `/health` every 3 seconds and updates the status chip as soon as the service comes back.
- **Restart** — equivalent to Stop; the portal waits for the service to come back online.

When the Orchestrator is **offline**, the portal displays a banner: *"Orchestrator is offline."* If `Portal:Orchestrator:SameHost = true` is configured, a **Start** button also appears that uses the Windows `ServiceController` API to start the local service. On separate-server deployments, start the service manually on its host.

### 14.7 Metrics and Scraping

The Orchestrator Service exposes three unauthenticated operations endpoints:

| Endpoint | Format | Purpose |
| :--- | :--- | :--- |
| `GET /health` | JSON | Liveness probe for supervisors and load balancers. |
| `GET /metrics` | JSON | Existing Portal/UI metrics payload with active jobs, queued jobs, maximum jobs, available slots, and active child processes. |
| `GET /metrics/prometheus` | Prometheus text | Scrape-friendly gauges for the same non-secret scheduler and process counts. |

`/metrics/prometheus` emits stable low-cardinality labels: `environment`, `node`, and
`component="orchestrator"`. It does not emit job names, script paths, script text, parameters,
connection metadata, credentials, or error details. Expose it only on a trusted management network or
behind your standard monitoring ingress controls.
In addition to live active/queued/max/available-slot gauges, the endpoint exports one-hour completed
job count, failed/interrupted count, average execution duration, rows processed, peak memory, CPU time,
and latest local-node host headroom samples for memory, CPU, state-disk free bytes, and spill-disk
free bytes when host metrics are available. Portal and Orchestrator Prometheus endpoints also export
component-labeled runtime gauges for process working set, private memory, managed heap bytes, and GC
collection counts.

These labels follow the shared `ETL_SQL.Core.Observability.ObservabilityConventions` contract used by
Portal metrics and traces. Prometheus labels drop the `etlsql.` prefix and replace dots with
underscores.

Ad-hoc jobs submitted through `POST /jobs` also emit `System.Diagnostics.ActivitySource` spans from
`ETL-SQL.Orchestrator.Service` and `System.Diagnostics.Metrics` instruments from the same-named
meter. The `orchestrator.job` span carries job id, request correlation id, environment, node,
component, workload kind, execution mode, status, rows, peak memory, and CPU time for trace
correlation. Metrics cover completed job count, duration, rows processed, peak memory, and CPU time
with low-cardinality tags only: environment, node, component, workload kind, execution mode, and status.
Scheduled and manually triggered persisted jobs emit the same pattern from `ETL-SQL.Orchestrator`:
the `orchestrator.scheduled_job` span carries the durable history id as `etlsql.job.id`, script
hash, attempt number, status, rows, peak memory, and CPU time, while metrics keep job id and script
hash out of labels and include environment, node, component, workload kind, execution mode, and status.
Enterprise policy refresh attempts emit spans from `ETL-SQL.Orchestrator.Policy` and metrics from the
same-named meter. The `orchestrator.policy_refresh` span carries terminal status plus policy version
and policy hash when a refresh succeeds; metrics report refresh count and duration with only
environment, node, component, workload kind, and status labels.
In-process ETL-SQL engine executions emit spans from `ETL-SQL.Engine` and metrics from the same-named
meter. The `engine.execution` span carries script hash, optional job correlation, rows, peak memory,
CPU time, spill bytes, and spill-read bytes; the metrics report execution count, duration, rows,
memory, CPU, spill writes, and spill reads with environment, node, component, execution mode, workload
kind, and status labels only.
Registered connectors emit spans from `ETL-SQL.Connectors` and metrics from the same-named meter for
version, catalog, and data-source creation operations. Connector telemetry uses connector type,
operation, status, environment, node, and component labels only; it intentionally omits connection
strings, hosts, table names, SQL text, paths, and credentials.
Dataset registry operations emit spans from `ETL-SQL.Datasets` and metrics from the same-named meter
for register, lookup, list, authorization, refresh-job registration, audit, delete, and path-build
operations. Dataset ids are trace-only; metrics use operation, status, environment, node, and component
labels and omit dataset names, caller permission strings, paths, and credentials.
Portal admin background services emit spans from `ETL-SQL.BackgroundServices` and metrics from the
same-named meter for native failure-digest, backup-report, and capacity-report runs. These metrics
use stable service name, operation, workload kind, status, environment, node, and component labels;
they do not include notification recipients, SMTP aliases, message bodies, run details, paths, or
error text.
The Portal orchestrator poller uses the same background-service meter/source for each poll cycle,
with statuses for degraded, idle, success, and failure. Poller labels intentionally omit
Orchestrator database paths, job names, subscription ids, report script paths, and error text.
Operational metrics digest sends also use `ETL-SQL.BackgroundServices`, with sent, skipped, and
failed statuses. Digest telemetry omits recipients, SMTP aliases, alert text, metric snapshot
content, message bodies, and delivery errors from labels.
Refresh-token maintenance emits purge-cycle spans and metrics from `ETL-SQL.BackgroundServices`.
The span carries the deleted-row count, while metrics keep only service name, operation, status,
workload kind, environment, node, and component labels and never include token hashes or usernames.
Audit-retention purges follow the same pattern: deleted-row counts are trace-only, and labels omit
audit actions, resource ids, details, actor names, and any retained or purged audit payload text.
Audit outbox transport drain and prune cycles emit background-service spans/metrics with delivered,
failed, empty, saturated, and success statuses as applicable. Row counts are trace-only; metric
labels omit event ids, audit actions, resource ids, payload JSON, bearer tokens, endpoints, and
transport error text.
Node heartbeat renewals emit background-service spans/metrics from `ETL-SQL.BackgroundServices`
with the host role as component and `node-heartbeat` as service name. Labels omit node ids,
machine names, heartbeat metadata JSON, capacity values, and lease-loss reasons.
Portal snapshot startup migration emits background-service spans/metrics with success, skipped,
cancelled, or failure status. Migrated snapshot counts are trace-only; labels omit manifest paths,
snapshot artifact keys, report names, and manifest payload values.
Portal startup validators for JWT secrets, dataset at-rest keys, OIDC configuration, and session-cache
lifecycle emit background-service spans/metrics with bounded service/operation/status labels only.
Secret values, validation messages, script paths, report ids, user ids, and session keys are omitted.
The Orchestrator host start/stop wrapper emits the same background-service span/metric contract with
component, service, operation, and status labels only; scheduler configuration and database paths are
not exported as labels.

Every Orchestrator HTTP response includes `X-Correlation-ID`, matching ASP.NET Core's request trace
identifier. Request logs are scoped with that correlation id and the active trace id so API calls,
job logs, and external monitoring traces can be joined during incident review.

### 14.8 Differences from `DROP JOB`

| Action | Effect |
| :--- | :--- |
| **Disable** (portal) | Sets `IsEnabled = 0`. Job stays in the database; history is preserved; the job is still visible in the portal greyed-out. Re-enable at any time. |
| **Delete** (portal) | Equivalent to `DROP JOB` — permanently removes the job definition and all history. Cannot be undone. |

Use **Disable** when you want to pause a recurring job temporarily. Use **Delete** only when you are retiring a job permanently.

---

*For the scheduling internals, see [Architecture/Orchestrator.md](./Architecture/Orchestrator.md).*  
*For the full `CREATE JOB` syntax and all scheduling options, see [Reference/Grammar.md](./Reference/Grammar.md#13-job-scheduling).*  
*For complete function and connector references, see [Reference/Standard_Library.md](./Reference/Standard_Library.md) and [Reference/Data_Connectors.md](./Reference/Data_Connectors.md).*
