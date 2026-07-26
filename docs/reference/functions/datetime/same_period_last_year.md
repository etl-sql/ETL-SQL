# SAME_PERIOD_LAST_YEAR

Returns the corresponding date exactly one year earlier, for year-over-year comparisons.

## Syntax

```sql
SAME_PERIOD_LAST_YEAR(date)
```

## Parameters

- **date** - Reference date.

## Returns

Returns the same calendar date in the previous year, preserving any time component.

## Null Behavior

Returns `NULL` when `date` is `NULL` or cannot be interpreted as a date.

## Remarks

- 29 February maps to 28 February in a non-leap year, matching `DATEADD('year', -1, date)`.
- The result is a calendar-aligned comparison, not a 365-day offset, so it does not align weekdays.
  Compare on a week or ISO-week key instead when day-of-week alignment matters.

## Examples

```sql
SELECT SAME_PERIOD_LAST_YEAR('2026-07-26') AS prior_year_date;
-- 2025-07-26
```

```sql
-- Year-over-year revenue comparison
SELECT
  c.Date,
  c.revenue                     AS revenue_this_year,
  p.revenue                     AS revenue_last_year
FROM #daily_revenue c
LEFT JOIN #daily_revenue p
  ON p.Date = SAME_PERIOD_LAST_YEAR(c.Date);
```

## References

- [Functions](../README.md)
- [DATEADD](dateadd.md)
- [DATEDIFF](datediff.md)
