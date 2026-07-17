# SHOW JOBS
Displays active and pending background or scheduled jobs.

## Syntax
```sql
SHOW JOBS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set listing each job with its name, status, schedule, last run time, and next run time.

## Example
```sql
-- View all active and pending jobs
SHOW JOBS;

-- Capture and filter
SHOW JOBS INTO #jobs;
SELECT JobName, Status, NextRunTime FROM #jobs WHERE Status = 'Running';
```

## Notes
- Shows jobs managed by the engine session and, when connected to an Orchestrator, jobs registered there.
- See also: `SHOW JOB HISTORY`, `SHOW JOB STATE`.

## References
- [SHOW Commands](README.md)
