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

## Remarks

- Month and weekday names are English on every server. Use [`FORMAT`](../string/format.md) with a culture for names in another language.
- Text is read as a date the way [`CAST`](../conversion/cast.md) reads it, on every server: ISO first, then slash dates in the [`SET DATEFORMAT`](../../set-commands/set-dateformat.md) order (default month first: `08/01/2024` is 1 August).

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

- [Functions](../README.md)
- [Datepart values](../../../syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- [DATEPART](datepart.md)
- [FORMAT](../string/format.md)
