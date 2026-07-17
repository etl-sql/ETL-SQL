# DATEADD

Adds a specified number of date/time units to a date or datetime value.

## Syntax

```sql
DATEADD(datepart, number, date)
```

## Parameters

- **datepart** - Unit to add. See [datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract).
- **number** - Number of units to add. Use a negative value to subtract.
- **date** - Base date or datetime value.

## Returns

Returns the resulting `DATETIME` after adding the interval.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT DATEADD(MONTH, 3, '2025-01-15') AS due_date;
```

```sql
SELECT order_id, DATEADD(YEAR, 1, order_date) AS warranty_expires
FROM #orders;
```

## References

- [Functions](../README.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATEDIFF](datediff.md)
- [DATEPART](datepart.md)
- [DATENAME](datename.md)
