# START_OF_WEEK

Returns the Sunday that begins the week containing a given date.

## Syntax

```sql
START_OF_WEEK(date)
```

## Parameters

- **date** - Reference date.

## Returns

Returns midnight on the preceding (or same-day) **Sunday** as a `DATE`. Any time component in the
input is discarded.

## Null Behavior

Returns `NULL` when `date` is `NULL` or cannot be interpreted as a date.

## Remarks

- **The week always starts on Sunday.** This is fixed and does not follow the host locale or
  `DATEFIRST`, so results are stable across machines and platforms.
- For an ISO week (Monday-based) key, use the `ISOWeek` column produced by
  [`GENERATE CALENDAR`](../../statements/data-prep.md).

## Examples

```sql
SELECT START_OF_WEEK('2026-07-26') AS week_start;
-- 2026-07-26 (a Sunday returns itself)
```

```sql
-- Weekly rollup
SELECT
  START_OF_WEEK(event_date) AS week,
  COUNT(*)                  AS events
FROM #events
GROUP BY START_OF_WEEK(event_date);
```

## References

- [Functions](../README.md)
- [END_OF_WEEK](end_of_week.md)
- [START_OF_MONTH](start_of_month.md)
