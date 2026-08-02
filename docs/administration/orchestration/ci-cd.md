# CI/CD Integration

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
| `CREATE JOB` (in-engine) | Both | Multiple inter-dependent recurring jobs, jobs that need ETL-SQL context, history tracking via `eng.job_history` |
| Windows Task Scheduler / `schtasks` | Windows | Single one-off scripts, OS-managed scheduling, no need to keep the engine running 24/7 |
| Linux crontab | Linux/macOS | Single scripts on a fixed schedule, minimal infrastructure, containerized environments |
| Windows Service (§9.1) + `CREATE JOB` | Windows | Production servers running `CREATE JOB` continuously with automatic restart |
| systemd service (§9.2) + `CREATE JOB` | Linux/macOS | Production servers running `CREATE JOB` continuously with automatic restart |

> [!TIP]
> **Rule of thumb:** If you have more than two or three recurring pipelines that share data or depend on each other, use `CREATE JOB` with the in-engine scheduler. For a single nightly script with no dependencies, OS-level scheduling (Task Scheduler or cron) is simpler and requires no long-running process.


---
