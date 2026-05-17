SHOW displays engine state — active connections, variables, execution profile, tables, jobs, and more. Add INTO #temp to query the results programmatically.

Syntax:
  SHOW <subject> [INTO #table];

Subjects:
  CONNECTIONS          — all registered data sources and their status
  CONNECTION <conn> CONFIG — configuration options for a specific connection (redacted)
  VARIABLES            — all declared variables in scope (SECRET vars masked)
  PROFILE              — per-statement timing (requires SET PROFILING = ON)
  JOBS                 — active and pending background or scheduled jobs
  TABLES [AT conn]     — tables available on a connection
  TAGS                 — lineage tags applied in the current session
  VERSION              — engine version and build metadata
  SUBSCRIPTIONS        — defined report subscriptions
  HISTORY              — recent job execution records

```sql
-- Inspect current variable state
SHOW VARIABLES;

-- List tables on a connection
SHOW TABLES AT SalesDB INTO #tbl_list;
SELECT table_name FROM #tbl_list WHERE table_name LIKE 'Order%';

-- Timing profile
SET PROFILING = ON;
SELECT * FROM dbo.LargeTable INTO #data;
SHOW PROFILE INTO #perf;
SELECT statement, duration_ms FROM #perf ORDER BY duration_ms DESC;

-- Check active jobs
SHOW JOBS;
```
