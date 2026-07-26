# END_OF_QUARTER

Returns the last day of the calendar quarter containing a given date.

## Syntax

```sql
END_OF_QUARTER(date)
```

## Parameters

- **date** - Reference date.

## Returns

Returns the last calendar day of that quarter as a `DATE` — 31 March, 30 June, 30 September, or
31 December.

## Null Behavior

Returns `NULL` when `date` is `NULL` or cannot be interpreted as a date.

## Remarks

- Quarters are **calendar** quarters starting in January. For a fiscal quarter, use the `FiscalQuarter`
  column produced by [`GENERATE CALENDAR`](../../statements/data-prep.md).

## Examples

```sql
SELECT END_OF_QUARTER('2026-07-26') AS quarter_end;
-- 2026-09-30
```

```sql
-- Bound a quarterly reporting window
SELECT *
FROM #ledger
WHERE entry_date BETWEEN START_OF_QUARTER(@as_of) AND END_OF_QUARTER(@as_of);
```

## References

- [Functions](../README.md)
- [START_OF_QUARTER](start_of_quarter.md)
- [END_OF_MONTH](end_of_month.md)
