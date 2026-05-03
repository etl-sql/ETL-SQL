DATE Functions
==============

Current Date and Time
---------------------
  GETDATE()             Current local datetime (date + time).
  NOW()                 Alias for GETDATE().
  CURRENT_TIMESTAMP     SQL-standard alias for GETDATE(); no parentheses needed.
  CURRENT_DATE          Today's date only (no time component).
  CURRENT_TIME          Current time only (no date component).

```sql
SELECT GETDATE()           -- e.g. 2025-03-15 09:42:17.123
SELECT CURRENT_DATE        -- 2025-03-15
SELECT CURRENT_TIME        -- 09:42:17
```

Extracting Parts as Integers
-----------------------------
  YEAR(d)               Extract the year (e.g. 2025).
  MONTH(d)              Extract the month number (1–12).
  DAY(d)                Extract the day of month (1–31).
  HOUR(d)               Extract the hour (0–23).
  MINUTE(d)             Extract the minute (0–59).
  SECOND(d)             Extract the second (0–59).
  DATEPART(part, d)     Extract any date part as an integer.

```sql
SELECT YEAR(GETDATE())                 -- 2025
SELECT MONTH('2025-08-20')             -- 8
SELECT DAY('2025-08-20')              -- 20
SELECT DATEPART(QUARTER, GETDATE())    -- 1, 2, 3, or 4
SELECT DATEPART(WEEKDAY, GETDATE())    -- 1=Sunday ... 7=Saturday
SELECT DATEPART(DAYOFYEAR, GETDATE()) -- 1–366
```

Valid DATEPART values:
  YEAR, YY, YYYY
  QUARTER, QQ, Q
  MONTH, MM, M
  WEEK, WK, WW
  DAY, DD, D
  WEEKDAY, DW
  DAYOFYEAR, DY, Y
  HOUR, HH
  MINUTE, MI, N
  SECOND, SS, S
  MILLISECOND, MS

Extracting Parts as Strings
----------------------------
  DATENAME(part, d)     Extract a date part as a string (e.g. day/month name).

```sql
SELECT DATENAME(MONTH, GETDATE())    -- 'March'
SELECT DATENAME(WEEKDAY, GETDATE())  -- 'Saturday'
SELECT DATENAME(YEAR, GETDATE())     -- '2025'
```

Date Arithmetic
---------------
  DATEADD(part, n, d)
      Add n units of part to date d. Use negative n to subtract.
      Returns the same type as d.

  DATEDIFF(part, start, end)
      Return the number of whole part units between start and end.
      Result is positive when end > start.

```sql
-- Add 3 months
SELECT DATEADD(MONTH, 3, '2025-01-15')    -- 2025-04-15

-- Subtract 7 days
SELECT DATEADD(DAY, -7, GETDATE())

-- Days since the beginning of the year
SELECT DATEDIFF(DAY, DATEFROMPARTS(YEAR(GETDATE()), 1, 1), GETDATE())

-- Full years between two dates (age calculation)
SELECT DATEDIFF(YEAR, '1990-06-01', GETDATE())

-- Hours between two timestamps
SELECT DATEDIFF(HOUR, '2025-03-15 08:00', '2025-03-15 13:30')  -- 5
```

Truncation and Rounding
------------------------
  DATETRUNC(part, d)
      Truncate d to the start of the given period.
      Returns a datetime with all lower-order parts set to their minimum values.

  EOMONTH(d [, offset])
      Return the last day of the month containing d.
      Optional offset: integer number of months to shift before finding end-of-month.

```sql
SELECT DATETRUNC(MONTH, '2025-03-15 14:30:00')  -- 2025-03-01 00:00:00
SELECT DATETRUNC(YEAR, GETDATE())                 -- 2025-01-01 00:00:00
SELECT DATETRUNC(WEEK, GETDATE())                 -- start of current week

SELECT EOMONTH(GETDATE())           -- last day of this month
SELECT EOMONTH(GETDATE(), 1)        -- last day of next month
SELECT EOMONTH('2025-02-01')        -- 2025-02-28
```

Constructing Dates and Times
-----------------------------
  DATETIMEFROMPARTS(y, mo, d, h, mi, s, ms)
      Build a datetime from its components. All arguments are integers.

  TIMEFROMPARTS(h, mi, s, ms, prec)
      Build a time value; prec is the fractional-seconds precision (0–7).

```sql
SELECT DATETIMEFROMPARTS(2025, 12, 31, 23, 59, 59, 0)
-- 2025-12-31 23:59:59.000

SELECT TIMEFROMPARTS(14, 30, 0, 0, 0)   -- 14:30:00
```

Validation
----------
  ISDATE(s)             Return 1 if s can be parsed as a valid date or datetime; 0 otherwise.

```sql
SELECT ISDATE('2025-03-15')     -- 1
SELECT ISDATE('not a date')     -- 0
SELECT ISDATE('2025-02-30')     -- 0  (Feb 30 does not exist)
```

Formatting
----------
  FORMAT(d, 'format_string')
      Convert a date/datetime to a string using a .NET date format pattern.

```sql
SELECT FORMAT(GETDATE(), 'yyyy-MM-dd')           -- '2025-03-15'
SELECT FORMAT(GETDATE(), 'dddd, MMMM d, yyyy')   -- 'Saturday, March 15, 2025'
SELECT FORMAT(GETDATE(), 'HH:mm:ss')             -- '09:42:17'
SELECT FORMAT(GETDATE(), 'MM/dd/yyyy hh:mm tt')  -- '03/15/2025 09:42 AM'
```

Common format tokens:
  yyyy  four-digit year          yy    two-digit year
  MM    two-digit month          MMM   abbreviated month name
  MMMM  full month name          dd    two-digit day
  ddd   abbreviated weekday      dddd  full weekday name
  HH    24-hour hour             hh    12-hour hour
  mm    minutes                  ss    seconds
  tt    AM/PM                    fff   milliseconds

CONVERT with Style Codes
------------------------
  CONVERT(type, value [, style])
      Convert value to type with an optional SQL Server style code for date formatting.

Common date style codes:
  101   MM/DD/YYYY              103   DD/MM/YYYY
  104   DD.MM.YYYY              110   MM-DD-YYYY
  112   YYYYMMDD                120   YYYY-MM-DD HH:MI:SS  (ISO)
  126   YYYY-MM-DDTHH:MI:SS     127   ISO 8601 with timezone

```sql
SELECT CONVERT(VARCHAR, GETDATE(), 101)   -- '03/15/2025'
SELECT CONVERT(VARCHAR, GETDATE(), 112)   -- '20250315'
SELECT CONVERT(VARCHAR, GETDATE(), 120)   -- '2025-03-15 09:42:17'
SELECT CONVERT(DATE, '20250315', 112)     -- date: 2025-03-15
```

Relative Dates (ETL-SQL RELDATE)
---------------------------------
For business-friendly relative date expressions see HELP RELDATE.
RELDATE tokens like TODAY, YESTERDAY, W, W-1, MTD, QTD, YTD etc.
honour the SET WEEK_START_DAY option. See HELP OPTIONS for details.
