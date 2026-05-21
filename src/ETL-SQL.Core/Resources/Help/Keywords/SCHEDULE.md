# CREATE JOB / Job Scheduling
Registers a job with the Core Orchestrator service.

## Syntax
```sql
CREATE JOB JobName
  ON SCHEDULE EVERY n unit [AT 'time']
  [WITH (option = value, ...)]
AS statement;
```

### Parameters
- **JobName**: A bare SQL identifier (no quotes).
- **EVERY n unit [AT 'time']**: The interval for the job schedule.
  - Supported units: `SECOND`, `SECONDS`, `MINUTE`, `MINUTES`, `HOUR`, `HOURS`, `DAY`, `DAYS`.
  - The `AT` clause (e.g. `AT '02:00'`) is only valid when the unit is `DAY` or `DAYS`.
- **WITH (option = value)**: Optional execution tuning.
  - `MAX_RETRIES` (integer, default 0): Retry attempts on failure.
  - `RETRY_DELAY` or `RETRY_DELAY_SECONDS` (integer, default 30): Delay between retries in seconds.
- **AS statement**: The ETL-SQL statement to run (often a `RUN SCRIPT` or `BEGIN ... END` block).
  - Unversioned published paths such as `orch://cleanup/main.etlsql` are resolved to the latest version when the job is created or altered, then stored as a pinned `orch://cleanup@version/main.etlsql` path.

## Examples
```sql
-- Run a script every 30 minutes
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'scripts/cleanup.etlsql';

-- Run the latest published cleanup bundle, pinned at job creation
CREATE JOB PublishedCleanup ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'orch://cleanup/main.etlsql';

-- Daily run at 02:00 with retries configured
CREATE JOB NightlyArchive 
  ON SCHEDULE EVERY 1 DAY AT '02:00'
  WITH (MAX_RETRIES = 3, RETRY_DELAY = 60)
AS
  RUN SCRIPT 'scripts/archive.etlsql';
```

## Remote Orchestrator Scheduling
To schedule a job on a remote orchestrator, execute the statement inside an `EXECUTE` block against the orchestrator connection:
```sql
EXECUTE orch_conn BEGIN
    CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
        RUN SCRIPT 'scripts/cleanup.etlsql';
END;
```

## Portal Refresh Jobs (Cron Scheduling)
Portal Refresh Jobs are distinct administrative tasks for updating report datasets. They support cron strings and must be run in the portal execution context:
```sql
EXECUTE portal_conn BEGIN
    CREATE REFRESH JOB FOR REPORT 'FinanceSales' SCHEDULE '0 2 * * *' AT orch_conn;
END;
```

## Job Management
```sql
-- List all configured jobs
SHOW JOBS;

-- List job execution history
SHOW JOB HISTORY;
SHOW JOB HISTORY NightlyArchive;

-- Query jobs by directing the output into a temp table
SHOW JOBS INTO #jobs;
SELECT * FROM #jobs WHERE MaxRetries > 0;

-- Drop a job (names must be unquoted; IF EXISTS is not supported)
DROP JOB CleanupJob;

-- Cancel a running job by its execution ID
KILL JOB 1023;
```

## Notes
- Job names are unique; attempting to create a job that already exists will result in an error. To update a job, run `DROP JOB` first and then `CREATE JOB`.
- Scheduling requires the ETL-SQL Orchestrator Service to be running.
- See: RUN SCRIPT, EXECUTE, SHOW
