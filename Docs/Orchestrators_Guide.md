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
# CREATE CONNECTION prod ON MSSQL('ENC:U2FsdGVkX1+...') WITH(TRUSTED_CONNECTION=FALSE);
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

### 2.7 Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Script completed successfully |
| `1` | Parse error, lint error, or runtime exception |

Exit codes are suitable for use in CI/CD pipeline gating.

---

## 3. Job Scheduling

Jobs are scheduled from within your `.etlsql` scripts using the `CREATE JOB` statement. Once registered, they are stored in a SQLite database and executed automatically by the background scheduler — no cron job or Windows Task Scheduler entry is required.

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

-- Run once daily at 2:00 AM
CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00' AS
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
CREATE JOB <name> ON SCHEDULE EVERY <n> SECONDS|MINUTES|HOURS|DAYS [AT 'HH:MM'] AS
    <single_statement>;

-- or with a block:
CREATE JOB <name> ON SCHEDULE EVERY <n> SECONDS|MINUTES|HOURS|DAYS [AT 'HH:MM'] AS
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

### 3.2 `SHOW JOBS` — List Registered Jobs

Displays all registered jobs with their schedule, last run time, and next scheduled run.

```sql
SHOW JOBS;

-- Or capture into a temp table for further processing
SHOW JOBS INTO #job_list;
SELECT * FROM #job_list WHERE IsEnabled = 1;
```

**Result columns:** `Name`, `Interval`, `Unit`, `AtTime`, `LastRun`, `NextRun`, `IsEnabled`

### 3.3 `SHOW JOB HISTORY` — View Execution History

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

**Status values:** `RUNNING`, `SUCCESS`, `FAILURE`

### 3.4 `DROP JOB` — Unschedule a Job

Removes a job definition and its full history from the store.

```sql
DROP JOB IF EXISTS CleanupJob;
DROP JOB NightlyArchive;
```

> [!CAUTION]
> `DROP JOB` permanently deletes the job and all its history entries. There is no undo. If you just want to pause a job temporarily, use the Orchestrator REST API: `PUT /jobs/{name}/disable` — or connect to `etlsql.db` with any SQLite tool and set `IsEnabled = 0`.

### 3.5 Cancelling a Running Job

To request cancellation of a currently-executing job, use the Orchestrator REST API:

```bash
# Get job run ID from SHOW JOB HISTORY (Id column, Status = 'RUNNING')
PUT http://localhost:5100/jobs/{name}/cancel
```

From within a script or TUI session, the in-engine scheduler will also automatically stop a job's execution if its `CancellationToken` is triggered (e.g. via `Ctrl+C` or process shutdown).

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

Host-level settings, including security limits, dashboard ports, and background service deployment (NSSM/systemd), are now managed in the central **[Administrators Guide](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Administrators_Guide.md)**.

Refer to that guide for:
- **`appsettings.json`** configuration keys.
- **Security Limits** (runaway protection).
- **Background Service** installation.
- **Resource Governance** (memory and disk spilling).


---

## 11. Troubleshooting

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

*For the scheduling internals, see [Architecture/Orchestrator.md](./Architecture/Orchestrator.md).*  
*For the full `CREATE JOB` syntax and all scheduling options, see [Reference/Grammar.md](./Reference/Grammar.md#13-job-scheduling).*  
*For complete function and connector references, see [Reference/Standard_Library.md](./Reference/Standard_Library.md) and [Reference/Data_Connectors.md](./Reference/Data_Connectors.md).*
