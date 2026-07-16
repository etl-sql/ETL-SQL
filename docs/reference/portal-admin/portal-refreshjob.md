# Portal Refresh Jobs
Schedule automatic dataset refresh for portal reports via the Orchestrator inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE REFRESH JOB FOR REPORT 'ReportName'
    SCHEDULE 'cron_expression'
    AT OrchestratorConnection;

  DROP REFRESH JOB FOR REPORT 'ReportName';
END;
```

## Examples
```sql
-- Refresh a report's datasets every night at 2 AM via the default orchestrator
EXECUTE portal BEGIN
  CREATE REFRESH JOB FOR REPORT 'Finance Dashboard'
    SCHEDULE '0 2 * * *'
    AT MyOrchConn;
END;

-- Refresh a high-frequency report every 15 minutes during business hours
EXECUTE portal BEGIN
  CREATE REFRESH JOB FOR REPORT 'Operations Monitor'
    SCHEDULE '*/15 8-18 * * 1-5'
    AT MyOrchConn;
END;

-- Refresh a weekly summary report every Sunday at midnight
EXECUTE portal BEGIN
  CREATE REFRESH JOB FOR REPORT 'Weekly Executive Summary'
    SCHEDULE '0 0 * * 0'
    AT MyOrchConn;
END;

-- Remove a refresh job
EXECUTE portal BEGIN
  DROP REFRESH JOB FOR REPORT 'Finance Dashboard';
END;

-- Check refresh job execution history
SHOW JOB HISTORY AT MyOrchConn INTO #history;
SELECT job_name, started_at, completed_at, status, error FROM #history
WHERE job_name LIKE 'Portal%'
ORDER BY started_at DESC;
```

## Notes
- Refresh jobs instruct the Orchestrator to periodically re-evaluate all datasets declared in the specified report on a cron schedule.
- The `AT` clause identifies the Orchestrator connection (a connector of type `ORCHESTRATOR`) that will own and execute the job. The connection must be defined and reachable.
- `SCHEDULE` uses standard 5-field cron syntax: `minute hour day-of-month month day-of-week`.
- The Orchestrator service must be running and reachable at the time the job fires. If the service is unavailable, the scheduled run is skipped and logged.
- Only one refresh job can be registered per report. Creating a new job for a report that already has one replaces the existing schedule.
- `DROP REFRESH JOB` removes the job from the Orchestrator's schedule. In-progress refresh runs are not interrupted.
- Use `SHOW JOB HISTORY` against the Orchestrator connection (not the portal) to monitor execution status, run durations, and errors.
- See: PORTAL_DATASET, PORTAL_REPORT, PORTAL_SHOW

References:
- [Data Connectors](../../guides/administration.md)
- [Portal Admin Commands](README.md)
