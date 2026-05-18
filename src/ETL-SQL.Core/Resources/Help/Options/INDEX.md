SET options configure engine behaviour for the current session. All options are reset when the session ends.
Syntax: SET <OPTION> ON|OFF  or  SET <OPTION> = <value>

Execution Mode
--------------
  SET WHAT_IF ON|OFF
      Dry-run mode. Side-effecting operations (INSERT, UPDATE, DELETE, MERGE, file writes,
      SEND EMAIL, Docker) are logged but not executed. SELECT, PRINT, DECLARE, and SET still run.
      Use to preview what a destructive script would do before running it for real.

  SET PROFILING ON|OFF
      Enable statement-level timing. View results with SHOW PROFILE [INTO #p].
      Each row shows the statement, duration in ms, and rows affected.

  SET SHOW_PASSWORD ON|OFF
      Unmask SENSITIVE/ENCRYPTED variables in SHOW VARIABLES output.
      USE PASSWORD values are never shown regardless of this setting.

Date / Locale
-------------
  SET WEEK_START_DAY = 'day'
      First day of the week for RELDATE week-boundary expressions (W, W-1, WE, etc.).
      Values: Monday (default), Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday.
      See HELP RELDATE for RELDATE expression syntax.

Performance Thresholds (override appsettings.json for this session)
-------------------------------------------------------------------
  SET BATCHSIZE = n                     Pipeline batch size (default 10,000 rows)
  SET JOIN_SPILL_THRESHOLD = n          Rows before a join spills to disk (default 100,000)
  SET WINDOW_SPILL_THRESHOLD = n        Rows before window functions spill (default 100,000)
  SET TEMP_TABLE_SPILL_THRESHOLD = n    Rows before a #temp spills to disk (default 1,000,000)
  SET EXTERNAL_HASH_PARTITIONS = n      Partitions for spilled hash operations (default 32)
  SET EXTERNAL_SORT_CHUNK_SIZE = n      Rows per sort chunk when spilling (default 50,000)
  SET MAX_LAST_RESULT_ROWS = n          Rows in the interactive display buffer (default 50,000)
  SET MAX_GENERATE_ROWS = n             Max rows GENERATE is allowed to produce (default 1,000,000)
  SET MAX_SMTP_EMAILS_PER_SCRIPT = n    Max SMTP emails a script may send (default 100)
  SET MAX_PARALLEL_DEGREE = n           Thread limit inside PARALLEL BEGIN...END (default: CPU count)
  SET FOREACH_PAGE_SIZE = n             Batch size when FOREACH iterates over a #temp table

Security Overrides (all produce an audit entry; path must be within a Safe Zone)
---------------------------------------------------------------------------------
  SET ALLOW_FILE_TYPE_ACCESS ON|OFF     Allow file extensions not in the global whitelist
  SET ALLOW_FILE_TYPE_ACCESS = '.ext'   Add a specific extension to the session whitelist
  SET ALLOW_FILE_OPERATIONS = n         Override runaway file-op protection limit (default 100)
  SET ALLOW_RECURSIVE_LAYERS = n        Override directory recursion depth limit (default 5)

Interactive
-----------
  SET WITH_PROMPT ON|OFF
      When ON, activating a SET marked with SET WITH_PROMPT will prompt for confirmation
      before applying (useful in PROD environment sets to prevent accidental activation).

```sql
-- Dry-run a destructive script
SET WHAT_IF ON;
DELETE FROM prod.OldOrders WHERE order_date < '2020-01-01';
-- outputs: [WHAT_IF] Would delete 14,832 rows from prod.OldOrders
SET WHAT_IF OFF;

-- Profile a slow SELECT
SET PROFILING ON;
SELECT region, SUM(amount) FROM prod.Sales GROUP BY region;
SHOW PROFILE INTO #timing;
SET PROFILING OFF;
SELECT * FROM #timing ORDER BY duration_ms DESC;

-- Raise spill threshold before a known large join
SET JOIN_SPILL_THRESHOLD = 500000;
```
