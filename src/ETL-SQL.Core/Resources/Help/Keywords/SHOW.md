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
  VIEWS                — session-scoped ETL-SQL query views
  TAGS                 — lineage tags applied in the current session
  VERSION              — engine version and build metadata
  SUBSCRIPTIONS        — defined report subscriptions
  HISTORY              — recent job execution records
  REPORT '<name>'      — portal report metadata
  REPORT HISTORY '<name>' — portal report refresh/history rows
  REPORT DEPENDENCIES '<name>' — dependencies discovered for a portal report
  SHARE LINKS FOR REPORT '<name>' — active portal share links
  EMBED TOKENS FOR REPORT '<name>' — portal embed tokens
  SAVED VIEWS FOR REPORT '<name>' — saved parameter views
  ALERTS FOR REPORT '<name>' — portal report alerts
  FAVORITES [FOR USER '<user>'] — portal favorites
  RECENT REPORTS       — recently viewed portal reports
  CATALOG SEARCH '<text>' — portal catalog search
  EFFECTIVE PERMISSIONS FOR USER|REPORT|FOLDER '<target>' — resolved portal permissions
  PORTAL USAGE METRICS — portal usage and refresh metrics
  ACTIVE SESSIONS      — unrevoked, unexpired portal refresh sessions

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

-- Check active jobs
SHOW JOBS;

EXECUTE portal BEGIN
  SHOW FAVORITES LIMIT 25 INTO #favorites;
  SHOW CATALOG SEARCH 'finance' INTO #catalog;
  SHOW ACTIVE SESSIONS;
END;
```

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
