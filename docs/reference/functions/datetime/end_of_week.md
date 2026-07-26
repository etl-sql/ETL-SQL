# END_OF_WEEK

Returns the Saturday that ends the week containing a given date.

## Syntax

```sql
END_OF_WEEK(date)
```

## Parameters

- **date** - Reference date.

## Returns

Returns the following (or same-day) **Saturday** as a `DATE` — equivalent to
`START_OF_WEEK(date) + 6 days`.

## Null Behavior

Returns `NULL` when `date` is `NULL` or cannot be interpreted as a date.

## Remarks

- **The week runs Sunday through Saturday.** This is fixed and does not follow the host locale or
  `DATEFIRST`, so results are stable across machines and platforms.
- For an ISO week (Monday-based) key, use the `ISOWeek` column produced by
  [`GENERATE CALENDAR`](../../statements/data-prep.md).

## Examples

```sql
SELECT END_OF_WEEK('2026-07-26') AS week_end;
-- 2026-08-01
```

```sql
-- Bound a weekly reporting window
SELECT *
FROM #activity
WHERE activity_date BETWEEN START_OF_WEEK(@as_of) AND END_OF_WEEK(@as_of);
```

## References

- [Functions](../README.md)
- [START_OF_WEEK](start_of_week.md)
- [END_OF_MONTH](end_of_month.md)
