# Portal Refresh Jobs
`CREATE REFRESH JOB` and `DROP REFRESH JOB` are retired Portal convenience commands. Use the unified Orchestrator catalog instead: create a `SCHEDULE`, create a `JOB ... FOR REPORT`, then attach the schedule with `ALTER JOB ... ADD SCHEDULE`.

## Syntax
```sql
EXECUTE orch_admin BEGIN
  CREATE SCHEDULE ScheduleName ON 'cron_expression';
  CREATE JOB JobName FOR REPORT '/Folder/ReportName';
  ALTER JOB JobName ADD SCHEDULE ScheduleName;

  DROP JOB IF EXISTS JobName;
END;
```

## Examples
```sql
-- Refresh a report's datasets every night at 2 AM
EXECUTE orch_admin BEGIN
  CREATE SCHEDULE FinanceNightly ON '0 2 * * *';
  CREATE JOB FinanceDashboardRefresh FOR REPORT '/Finance/Finance Dashboard';
  ALTER JOB FinanceDashboardRefresh ADD SCHEDULE FinanceNightly;
END;

-- Refresh a high-frequency report every 15 minutes during business hours
EXECUTE orch_admin BEGIN
  CREATE SCHEDULE OperationsBusinessHours ON '*/15 8-18 * * 1-5';
  CREATE JOB OperationsMonitorRefresh FOR REPORT '/Operations/Operations Monitor';
  ALTER JOB OperationsMonitorRefresh ADD SCHEDULE OperationsBusinessHours;
END;

-- Refresh a weekly summary report every Sunday at midnight
EXECUTE orch_admin BEGIN
  CREATE SCHEDULE ExecutiveWeekly ON '0 0 * * 0';
  CREATE JOB ExecutiveSummaryRefresh FOR REPORT '/Executive/Weekly Executive Summary';
  ALTER JOB ExecutiveSummaryRefresh ADD SCHEDULE ExecutiveWeekly;
END;

-- Remove a refresh job
EXECUTE orch_admin BEGIN
  DROP JOB IF EXISTS FinanceDashboardRefresh;
END;

-- Check refresh job execution history
SHOW JOB HISTORY AT orch_admin INTO #history;
SELECT job_name, started_at, completed_at, status, error FROM #history
WHERE job_name LIKE 'Finance%'
ORDER BY started_at DESC;
```

## Notes
- The old `CREATE REFRESH JOB FOR REPORT ... SCHEDULE ... AT ...` form is rejected with a diagnostic naming this replacement.
- Report refresh jobs are ordinary Orchestrator jobs whose target kind is `REPORT`.
- `SCHEDULE` uses standard 5-field cron syntax: `minute hour day-of-month month day-of-week`.
- The Orchestrator service must be running and reachable at the time the job fires. If the service is unavailable, the scheduled run is skipped and logged.
- A report can have multiple scheduled jobs by attaching multiple schedules or creating multiple jobs that target the same report.
- Use `SHOW JOB HISTORY` against the Orchestrator connection to monitor execution status, run durations, and errors.
- See: PORTAL_DATASET, PORTAL_REPORT, PORTAL_SHOW

References:
- [Job Orchestration](../orchestrator-jobs/schedule.md)
- [Portal Admin Commands](README.md)
