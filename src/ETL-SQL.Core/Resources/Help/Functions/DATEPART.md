# DATEPART
Extracts a specific date/time part from a date as an integer.

Syntax:
  DATEPART(part, date)

Parameters:
  part — the date/time unit to extract (YEAR, QUARTER, MONTH, DAY, HOUR, MINUTE, SECOND, MILLISECOND, WEEKDAY, DAYOFYEAR)
  date — the source date or datetime value

```sql
-- Get the current quarter
SELECT DATEPART(QUARTER, GETDATE());

-- Get the day of the week
SELECT DATEPART(WEEKDAY, '2025-03-15');
```
