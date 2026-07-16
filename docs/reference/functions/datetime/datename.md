# DATENAME

Returns the name of a specific date part as a string.

## Syntax

```sql
DATENAME(datepart, date)
```

## Parameters

- **datepart** - Date part to name. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **date** - Source date or datetime value.

## Returns

Returns the named date part as a `STRING`.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT DATENAME(MONTH, '2026-05-17') AS month_name;
```

```sql
SELECT DATENAME(WEEKDAY, order_date) AS weekday_name, COUNT(*) AS order_count
FROM #orders
GROUP BY DATENAME(WEEKDAY, order_date);
```

## References

- [Standard Library](../standard-library.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATEPART](datepart.md)
- [FORMAT](../general/format.md)
