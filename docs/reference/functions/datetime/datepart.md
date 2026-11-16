# DATEPART

Returns the integer value of a specific date part from a date or datetime.

## Syntax

```sql
DATEPART(datepart, date)
```

## Parameters

- **datepart** - Date part to extract. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **date** - Source date or datetime value.

## Returns

Returns an `INT` value for the specified date part.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Remarks

- Text is read as a date the way [`CAST`](../conversion/cast.md) reads it, on every server: ISO first, then slash dates in the [`SET DATEFORMAT`](../../set-commands/set-dateformat.md) order (default month first: `08/01/2024` is 1 August).

## Examples

```sql
SELECT DATEPART(YEAR, GETDATE()) AS current_year;
```

```sql
SELECT DATEPART(QUARTER, '2026-05-17') AS fiscal_quarter;
```

```sql
SELECT DATEPART(HOUR, order_time) AS order_hour, COUNT(*) AS order_count
FROM #orders
GROUP BY DATEPART(HOUR, order_time);
```

## References

- [Functions](../README.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATENAME](datename.md)
- [DATEADD](dateadd.md)
- [YEAR](year.md)
- [MONTH](month.md)
- [DAY](day.md)
