# DATEADD
Adds a specific number of date/time units to a date or datetime value.

Syntax:
  DATEADD(part, number, date)

Parameters:
  part   — the date/time unit to add (YEAR, QUARTER, MONTH, DAY, HOUR, MINUTE, SECOND, MILLISECOND)
  number — integer value to add (negative for subtraction)
  date   — the source date or datetime value

```sql
-- Add 3 months
SELECT DATEADD(MONTH, 3, '2025-01-15')    -- 2025-04-15

-- Subtract 7 days from today
SELECT DATEADD(DAY, -7, GETDATE())
```
