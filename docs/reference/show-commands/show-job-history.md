# SHOW JOB HISTORY
Displays recent job execution records — for all jobs or a single named job.

## Syntax
```sql
SHOW JOB HISTORY ['<job>'] [AT <conn>] [INTO #table];
```

## Parameters
- **'job'** — Optional. The name of a specific job. When omitted, shows history for all jobs.
- **AT conn** — Optional. Specifies the Orchestrator connection to query.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with job name, run ID, start time, end time, duration, status, and error message (if any) for each execution record.

## Example
```sql
-- View all recent job executions
SHOW JOB HISTORY;

-- View history for a specific job
SHOW JOB HISTORY 'nightly_etl';

-- Capture and find failures
SHOW JOB HISTORY INTO #hist;
SELECT JobName, StartTime, Status, ErrorMessage
FROM #hist
WHERE Status = 'Failed'
ORDER BY StartTime DESC;
```

## Notes
- Without a job name, returns recent history across all jobs.
- The depth of history returned depends on Orchestrator retention settings.
- See also: `SHOW JOBS`, `SHOW JOB STATE`.

## References
- [SHOW Commands](README.md)
