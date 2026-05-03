ETL-SQL Built-In Function Reference
=====================================

Functions are available in SELECT lists, WHERE clauses, SET assignments,
and anywhere an expression is valid.

Categories
----------
  STRING      Text manipulation — UPPER, TRIM, SUBSTRING, REPLACE, CONCAT, etc.
  DATE        Date/time arithmetic — GETDATE, DATEADD, DATEDIFF, FORMAT, etc.
  MATH        Numeric operations — ROUND, ABS, POWER, SQRT, LOG, trig, etc.
  AGGREGATE   Group operations — SUM, COUNT, AVG, MIN, MAX, STRING_AGG, etc.
  WINDOW      Analytic/ranking — ROW_NUMBER, RANK, LAG, LEAD, running totals, etc.
  JSON        JSON manipulation — JSON_VALUE, JSON_QUERY, JSON_MODIFY, OPENJSON, etc.
  NULL        NULL handling — ISNULL, COALESCE, NULLIF, IIF, CASE, etc.
  CONVERSION  Type casting — CAST, CONVERT, TRY_CAST, PARSE, HASHBYTES, etc.

Usage
-----
  HELP FUNCTIONS STRING       -- string function reference
  HELP FUNCTIONS DATE         -- date/time function reference
  HELP FUNCTIONS MATH         -- math and numeric functions
  HELP FUNCTIONS AGGREGATE    -- aggregate functions
  HELP FUNCTIONS WINDOW       -- window / analytic functions
  HELP FUNCTIONS JSON         -- JSON functions
  HELP FUNCTIONS NULL         -- NULL handling functions
  HELP FUNCTIONS CONVERSION   -- type conversion functions

Notes
-----
  - Function names are case-insensitive.
  - NULL propagates: any function receiving NULL usually returns NULL.
    See HELP FUNCTIONS NULL for COALESCE/ISNULL patterns.
  - Aggregate functions cannot appear in WHERE; use HAVING instead.
  - Window functions require an OVER clause and cannot be nested.
