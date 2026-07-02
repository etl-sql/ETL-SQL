# SHOW

SHOW displays engine state — active connections, variables, execution profile, tables, jobs, locks, and more. Add INTO #temp to query the results programmatically.

```sql
SHOW <subject> [INTO #table];
```

Subjects:
- **CONNECTIONS** — all registered data sources and their status.
- **CONNECTION <conn> CONFIG** — configuration options for a specific connection (redacted).
- **VARIABLES** — all declared variables in scope (SECRET vars masked).
- **PROFILE** — per-statement timing (requires SET PROFILING = ON).
- **JOBS** — active and pending background or scheduled jobs.
- **LOCKS** — active database/job throttle slots and concurrency queue details.
- **TABLES [AT conn]** — tables available on a connection.
- **VIEWS** — session-scoped ETL-SQL query views.
- **TAGS** — lineage tags applied in the current session.
- **VERSION** — engine version and build metadata.
- **SUBSCRIPTIONS** — defined report subscriptions.
- **JOB HISTORY ['<job>'] [AT conn]** — recent job execution records (all jobs, or one named job).
- **HOST METRICS ['<nodeId>']** — host-utilization time series for capacity planning: per node, the last 24 hours of memory-load %, CPU %, and free disk (MB) on the state and spill volumes, newest first. Optionally filter to one node id.
- **REPORT '<name>'** — portal report metadata.
- **REPORT HISTORY '<name>'** — portal report refresh/history rows.
- **REPORT DEPENDENCIES '<name>'** — dependencies discovered for a portal report.
- **SHARE LINKS FOR REPORT '<name>'** — active portal share links.
- **EMBED TOKENS FOR REPORT '<name>'** — portal embed tokens.
- **SAVED VIEWS FOR REPORT '<name>'** — saved parameter views.
- **ALERTS FOR REPORT '<name>'** — portal report alerts.
- **FAVORITES [FOR USER '<user>']** — portal favorites.
- **RECENT REPORTS** — recently viewed portal reports.
- **CATALOG SEARCH '<text>'** — portal catalog search.
- **EFFECTIVE PERMISSIONS FOR USER|REPORT|FOLDER '<target>'** — resolved portal permissions.
- **PORTAL USAGE METRICS** — portal usage and refresh metrics.
- **ACTIVE SESSIONS** — unrevoked, unexpired portal refresh sessions.

Examples:
```sql
-- Inspect current variable state
SHOW VARIABLES;

-- List tables on a connection
SHOW TABLES AT SalesDB INTO #tbl_list;
SELECT table_name FROM #tbl_list WHERE table_name LIKE 'Order%';

-- List session query views
SHOW VIEWS INTO #views;
SELECT Name, Query FROM #views;

-- Timing profile
SET PROFILING = ON;
SELECT * FROM dbo.LargeTable INTO #data;
SHOW PROFILE INTO #perf;
SELECT statement, duration_ms FROM #perf ORDER BY duration_ms DESC;

-- Check active locks and queue wait times
SHOW LOCKS;

-- Check active jobs
SHOW JOBS;

-- Capacity planning: find nodes low on spill/state disk in the last 24h
SHOW HOST METRICS INTO #hm;
SELECT NodeId, MIN(StateDiskFreeMB) AS MinStateFreeMB, MIN(SpillDiskFreeMB) AS MinSpillFreeMB,
       MAX(MemoryLoadPercent) AS PeakMemPct
FROM #hm
GROUP BY NodeId;

EXECUTE portal BEGIN
  SHOW FAVORITES LIMIT 25 INTO #favorites;
  SHOW CATALOG SEARCH 'finance' INTO #catalog;
  SHOW ACTIVE SESSIONS;
END;
```

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
