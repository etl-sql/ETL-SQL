# START_OF_QUARTER

Returns the first day of the calendar quarter containing a given date.

## Syntax

```sql
START_OF_QUARTER(date)
```

## Parameters

- **date** - Reference date.

## Returns

Returns midnight on the first day of that quarter as a `DATE` — 1 January, 1 April, 1 July, or
1 October. Any time component in the input is discarded.

## Null Behavior

Returns `NULL` when `date` is `NULL` or cannot be interpreted as a date.

## Remarks

- Quarters are **calendar** quarters starting in January. For a fiscal quarter, use the `FiscalQuarter`
  column produced by [`GENERATE CALENDAR`](../../statements/data-prep.md).

## Examples

```sql
SELECT START_OF_QUARTER('2026-07-26') AS quarter_start;
-- 2026-07-01
```

```sql
-- Quarter-to-date total
SELECT SUM(amount) AS qtd_total
FROM #orders
WHERE order_date >= START_OF_QUARTER(GETDATE());
```

## References

- [Functions](../README.md)
- [END_OF_QUARTER](end_of_quarter.md)
- [START_OF_MONTH](start_of_month.md)
