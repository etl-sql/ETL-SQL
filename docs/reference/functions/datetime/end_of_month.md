# END_OF_MONTH

Returns the last day of the month for a given date, optionally offset by a number of months.
`END_OF_MONTH` is an alias for [`EOMONTH`](eomonth.md).

## Syntax

```sql
END_OF_MONTH(date)
END_OF_MONTH(date, months)
```

## Parameters

- **date** - Reference date.
- **months** - Optional month offset applied before computing end-of-month. Use negative values for prior months.

## Returns

Returns the last calendar day of the selected month as a `DATE`.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Remarks

- Provided as the symmetric counterpart to [`START_OF_MONTH`](start_of_month.md); prefer whichever
  naming keeps a script readable. `EOMONTH` remains available for SQL Server familiarity.

## Examples

```sql
SELECT END_OF_MONTH('2026-02-10') AS month_end;
-- 2026-02-28
```

```sql
SELECT END_OF_MONTH(GETDATE(), -1) AS previous_month_end;
```

```sql
-- Bound a reporting period
SELECT *
FROM #ledger
WHERE entry_date BETWEEN START_OF_MONTH(@period) AND END_OF_MONTH(@period);
```

## References

- [Functions](../README.md)
- [EOMONTH](eomonth.md)
- [START_OF_MONTH](start_of_month.md)
