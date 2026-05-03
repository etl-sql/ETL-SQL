# SCHEDULE
Manages scheduled ETL-SQL jobs: create, list, and terminate scheduled script executions.

## SCHEDULE JOB — create or update a scheduled job
```sql
SCHEDULE JOB 'DailySalesLoad'
  RUN SCRIPT 'jobs/load_sales.etlsql'
  EVERY '0 6 * * *';           -- cron: daily at 06:00

-- With parameters
SCHEDULE JOB 'WeeklyReport'
  RUN SCRIPT 'reports/weekly.etlsql'
  WITH (@region = 'North')
  EVERY '0 8 * * MON';         -- cron: Mondays at 08:00

-- One-shot future run
SCHEDULE JOB 'MigrationOnce'
  RUN SCRIPT 'migrations/v2.etlsql'
  AT '2025-06-01 02:00:00';
```

## SHOW JOBS — list scheduled jobs
```sql
SHOW JOBS;                     -- all jobs
SHOW JOBS WHERE Status = 'Active';
```

## KILL JOB — stop a running or scheduled job
```sql
KILL JOB 'DailySalesLoad';
```

## Cron quick reference
| Expression | Meaning |
|---|---|
| `0 6 * * *` | Daily at 06:00 |
| `0 8 * * MON` | Mondays at 08:00 |
| `*/15 * * * *` | Every 15 minutes |
| `0 0 1 * *` | First day of each month |

## Notes
- Job names are unique; re-issuing `SCHEDULE JOB` with the same name updates the existing job.
- Scheduling requires the ETL-SQL Orchestrator Service to be running.
- Execution history is stored in the Orchestrator SQLite database and viewable via `SHOW JOBS`.
- See: RUN SCRIPT, SHOW