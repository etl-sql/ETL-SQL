# SHOW JOB STATE
Displays saved job-state key/value pairs written by `SET_JOB_STATE` — watermarks, backup markers, and other persistent job metadata.

## Syntax
```sql
SHOW JOB STATE ['<job>'] [INTO #table];
```

## Parameters
- **'job'** — Optional. The name of a specific job. When omitted, shows state for all jobs.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with columns: `JobName`, `StateKey`, `StateValue`, `UpdatedAt`.

## Example
```sql
-- Inspect a job's watermarks and backup markers
SHOW JOB STATE 'nightly_backup_report' INTO #st;
SELECT StateKey, StateValue, UpdatedAt FROM #st;

-- View all job state across the Orchestrator
SHOW JOB STATE;
```

## Notes
- Lists every key for any Orchestrator-managed job — unlike `GET_JOB_STATE`, which reads one known key in the caller's own context.
- CLI-run scripts keep their state in a local `.etlstate` file, which this command does not show.
- See also: `SHOW JOBS`, `SHOW JOB HISTORY`.

## References
- [SHOW Commands](README.md)
