SYSTEM Functions
================

Utility functions for list manipulation, number series generation, identity values,
error inspection, and environment variable access.

List Functions
--------------
  APPEND_TO_LIST(@list, value)    Add value to the end of a LIST variable. Returns the new list.
  ADD_TO_LIST(@list, value)       Alias for APPEND_TO_LIST.
  REMOVE_FROM_LIST(@list, value)  Remove all occurrences of value from the list.
  SORT_LIST(list)                 Return the list sorted ascending.
  SORT_LIST(list, 'DESC')         Return the list sorted descending.
  LENGTH(list)                    Return the number of items in a list.
  COUNT(list)                     Alias for LENGTH when applied to a list.

```sql
DECLARE @tags LIST(VARCHAR) = LIST('alpha', 'beta');

SET @tags = APPEND_TO_LIST(@tags, 'gamma');
-- @tags = ['alpha', 'beta', 'gamma']

SET @tags = REMOVE_FROM_LIST(@tags, 'beta');
-- @tags = ['alpha', 'gamma']

SELECT LENGTH(@tags)             -- 2
SELECT SORT_LIST(@tags)          -- ['alpha', 'gamma']
SELECT SORT_LIST(@tags, 'DESC')  -- ['gamma', 'alpha']
```

Series Generation
-----------------
  GENERATE_SERIES(start, stop)          Return a list of integers from start to stop inclusive.
  GENERATE_SERIES(start, stop, step)    Use step increment (default 1; negative steps count down).

```sql
-- Insert 100 test rows
INSERT INTO #numbers (n)
SELECT value FROM GENERATE_SERIES(1, 100);

-- Every 5th number
SELECT value FROM GENERATE_SERIES(0, 50, 5);
-- 0, 5, 10, ..., 50

-- Countdown
SELECT value FROM GENERATE_SERIES(10, 1, -1);
-- 10, 9, 8, ..., 1
```

Identity and Uniqueness
-----------------------
  NEWID()               Return a new UUID v7 (time-ordered, globally unique).
  NEWSEQUENTIALID()     Alias for NEWID(); always sequential within a session.

```sql
INSERT INTO #events (event_id, name)
VALUES (NEWID(), 'user_login');

-- Generate a batch key for correlation
DECLARE @batch_id VARCHAR = NEWID();
```

Error Context (inside CATCH blocks)
-------------------------------------
  ERROR_NUMBER()     Error number that triggered the CATCH block.
  ERROR_MESSAGE()    Full error message text.
  ERROR_SEVERITY()   Severity level (1–25).
  ERROR_STATE()      State number associated with the error.
  ERROR_LINE()       Line number where the error occurred.

```sql
BEGIN TRY
    SELECT 1 / 0;
END TRY
BEGIN CATCH
    PRINT 'Error ' + TO_STR(ERROR_NUMBER()) + ': ' + ERROR_MESSAGE();
    PRINT 'Severity ' + TO_STR(ERROR_SEVERITY()) + ', line ' + TO_STR(ERROR_LINE());
END CATCH;
```

Environment Variables
---------------------
  ENV('VAR_NAME')    Return the value of a host environment variable.
                     Access is subject to the security allow-list in appsettings.json.
                     Returns NULL if the variable is not set or is not permitted.

```sql
-- Use environment-specific path
DECLARE @root VARCHAR = ENV('ETL_DATA_ROOT');
IF @root IS NULL SET @root = 'C:\data';

SELECT * FROM FILE_LIST(@root + '\input');
```

Notes
-----
  - LIST variables are declared with DECLARE @name LIST(type). See HELP DECLARE.
  - GENERATE_SERIES caps output at 1 000 000 elements to prevent runaway queries.
  - NEWID() uses UUID v7 which is sortable by creation time, suitable for primary keys.
  - ENV() is restricted by Engine.Security.AllowedEnvironmentVariables in appsettings.json.
  - For type conversion utilities see HELP FUNCTIONS CONVERSION.
