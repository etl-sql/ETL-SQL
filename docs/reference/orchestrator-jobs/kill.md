# KILL
Cancels a running background job by its execution ID.

## Syntax
```sql
KILL JOB <execution_id>;
```

## Examples
```sql
-- Cancel a specific job by known ID
KILL JOB 1042;

-- Find the most recently started running job and cancel it
SELECT * INTO #jobs FROM eng.jobs;
DECLARE @id INT = (SELECT TOP 1 execution_id FROM #jobs WHERE status = 'RUNNING' ORDER BY started_at DESC);
KILL JOB @id;

-- Cancel all running jobs for a specific script
SELECT * INTO #jobs FROM eng.jobs;

FOREACH @id IN (SELECT execution_id FROM #jobs WHERE status = 'RUNNING' AND script_name = 'nightly-load.etlsql')
BEGIN
  KILL JOB @id;
END;
```

## Notes
- The `execution_id` is available from `eng.jobs` and `eng.job_history`.
- KILL sends a cancellation signal — the job may not stop immediately if it is in a non-cancellable I/O operation. Query `eng.jobs` after a moment to confirm the status transitions to `CANCELLED`.
- The job's status in Orchestrator history is updated to `CANCELLED` once the cancellation is processed.
- Cannot cancel jobs running on remote Orchestrators directly — use `EXECUTE <orch_conn> BEGIN KILL JOB <id>; END` for remote cancellation.
- Cancelling a job that has already completed or does not exist produces a warning, not an error.
- See: SHOW, SCHEDULE, EXECUTE

References:
- [Orchestrator Jobs](README.md)
