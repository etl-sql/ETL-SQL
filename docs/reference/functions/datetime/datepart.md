# DATEPART

Returns the integer value of a specific date part from a date or datetime.

## Syntax

```sql
DATEPART(datepart, date)
```

## Parameters

- **datepart** - Date part to extract. See [datepart values](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **date** - Source date or datetime value.

## Returns

Returns an `INT` value for the specified date part.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

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

- [Standard Library](../standard-library.md)
- [Datepart values](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATENAME](datename.md)
- [DATEADD](dateadd.md)
- [YEAR](year.md)
- [MONTH](month.md)
- [DAY](day.md)
