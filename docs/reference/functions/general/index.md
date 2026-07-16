ETL-SQL Built-In Function Reference
=====================================

Functions are available in SELECT lists, WHERE clauses, SET assignments,
and anywhere an expression is valid.

Categories
----------
  STRING      Text manipulation
              UPPER, LOWER, TRIM, LTRIM, RTRIM, LEN, LENGTH, CHAR_LENGTH, LEFT, RIGHT, SUBSTRING,
              SUBSTR, CHARINDEX, INSTR, PATINDEX, POSITION, REPLACE, REMOVE_HIDDEN_CHARACTERS,
              REMOVE_HTML_CHARACTERS, STUFF, TRANSLATE, REVERSE,
              REPLICATE, SPACE, CONCAT, CONCAT_WS, STRING_SPLIT, STR, SOUNDEX, DIFFERENCE,
              QUOTENAME, STRING_ESCAPE, ASCII, UNICODE, CHAR, OVERLAY

  DATE        Date/time arithmetic
              GETDATE, NOW, CURRENT_TIMESTAMP, CURRENT_DATE, CURRENT_TIME, YEAR, MONTH, DAY,
              HOUR, MINUTE, SECOND, DATEPART, DATENAME, DATEADD, DATEDIFF, DATETRUNC, EOMONTH,
              DATETIMEFROMPARTS, TIMEFROMPARTS, ISDATE, FORMAT

  MATH        Numeric operations
              ABS, ROUND, TRUNCATE, TRUNC, CEILING, CEIL, FLOOR, SIGN, POWER, POW, SQRT, EXP,
              LOG, LOG10, SIN, COS, TAN, ASIN, ACOS, ATAN, ATAN2, DEGREES, RADIANS, PI, MOD,
              QUOTIENT, RANDOM, RAND, RANDOM_INT, RANDOM_DECIMAL

  AGGREGATE   Group operations
              SUM, COUNT, AVG, MIN, MAX, STRING_AGG, LISTAGG, STDEV, STDEVP, VAR, VARP,
              MEDIAN, PERCENTILE_CONT, PERCENTILE_DISC

  WINDOW      Analytic/ranking
              ROW_NUMBER, RANK, DENSE_RANK, NTILE, LAG, LEAD, FIRST_VALUE, LAST_VALUE

  JSON        JSON manipulation
              JSON_VALUE, JSON_QUERY, JSON_MODIFY, ISJSON

  XML         XML manipulation
              XMLVALUE, XMLEXISTS, XMLQUERY

  REGEX       Regular expressions
              REGEXP_LIKE, REGEXP_SUBSTR, REGEXP_REPLACE, REGEXP_INSTR

  FILE        File system
              FILE_LIST, DIRECTORY, FILE_EXISTS, DIRECTORY_EXISTS, REMOTE_FILE_LIST

  NULL        NULL handling
              ISNULL, IFNULL, NVL, COALESCE, NULLIF, NVL2, IIF, DECODE, IS_NULL, IS_NOT_NULL,
              GREATEST, LEAST

  CONVERSION  Type casting
              CAST, TRY_CAST, CONVERT, TRY_CONVERT, PARSE, TRY_PARSE, TO_STR, HASHBYTES,
              CHECKSUM, BINARY_CHECKSUM

  SYSTEM      Utilities
              APPEND_TO_LIST, ADD_TO_LIST, REMOVE_FROM_LIST, SORT_LIST, GENERATE_SERIES,
              NEWID, NEWSEQUENTIALID, ERROR_NUMBER, ERROR_MESSAGE, ERROR_SEVERITY,
              ERROR_STATE, ERROR_LINE, ENV

Usage
-----
  HELP [FUNCTION_NAME]        -- Get detailed help for a specific function (e.g. HELP DATEADD)
  HELP FUNCTIONS              -- Show this index

Notes
-----
  - Function names are case-insensitive.
  - NULL propagates: any function receiving NULL usually returns NULL.
    See HELP FUNCTIONS NULL for COALESCE/ISNULL patterns.
  - Aggregate functions cannot appear in WHERE; use HAVING instead.
  - Window functions require an OVER clause and cannot be nested.

References:
- [Standard Library](../../../guides/getting-started.md)
