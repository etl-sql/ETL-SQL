SET assigns a value to a declared variable or changes an engine execution option.

Syntax — variable assignment:
  SET @variable = <expression>;

Syntax — engine option:
  SET <OPTION> = ON | OFF | <value>;

Engine options:
  WHAT_IF = ON|OFF              — parse and plan without executing DML (default OFF)
  PROFILING = ON|OFF            — collect per-statement timing; view with SHOW PROFILE
  SHOW_PASSWORD = ON|OFF        — include passwords in SHOW CONNECTIONS output (default OFF)
  BATCHSIZE = n                 — rows per remote fetch batch for SELECT ... FROM connection
  WEEK_START_DAY = '<day>'      — anchor day for RELDATE week expressions (default Monday)
                                   Valid: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday
  JOIN_SPILL_THRESHOLD = n      — row count before hash join spills to disk
  SORT_SPILL_THRESHOLD = n      — row count before sort spills to disk

```sql
-- Variable assignment
SET @region  = 'North';
SET @cutoff  = DATEADD(DAY, -30, GETDATE());

-- Dry-run mode: parse and plan only, no DML executed
SET WHAT_IF = ON;

-- Collect timing
SET PROFILING = ON;
SELECT * FROM dbo.BigTable INTO #data;
SHOW PROFILE;

-- Override week start for RELDATE expressions
SET WEEK_START_DAY = 'Sunday';
```
