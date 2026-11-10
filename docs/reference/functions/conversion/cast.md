# CAST

Converts an expression to a target ETL-SQL data type and raises an error when the value cannot be converted.

## Syntax

```sql
CAST(expression AS type)
```

## Parameters

- **expression** - The value or expression to convert.
- **type** - The target ETL-SQL data type, such as `INT`, `BIGINT`, `DECIMAL(p,s)`, `VARCHAR(n)`, `DATE`, `DATETIME`, `BOOLEAN`, or `VARBINARY`.

## Returns

Returns the converted value using the requested target type.

## Null Behavior

`CAST(NULL AS type)` returns `NULL` with the requested type. Non-null values that cannot be converted raise an execution error.

## Remarks

- Use `CAST` when invalid data should stop the script.
- Casting to a string type gives the same text on every server, whatever its locale: dates are ISO `yyyy-MM-dd HH:mm:ss` (`2024-01-08 00:00:00`; fractional seconds only when present, then the offset for a `DATETIMEOFFSET`) and numbers use `.` as the decimal point. Use [`FORMAT`](../string/format.md) for a locale-specific layout.
- Text cast to a date type reads the same on every server: ISO first (`2024-01-08`, `2024-01-08 09:30:05`), then slash dates month first as in SQL Server and PostgreSQL (`08/01/2024` is 1 August). A slash date whose first number cannot be a month (`25/01/2024`) reads day first. [`SET DATEFORMAT DMY`](../../set-commands/set-dateformat.md) reads slash dates day first. Month names are English only. Every engine path that reads text as a date (`DATEPART`, `DATENAME`, `EXTRACT`, `DATEDIFF`, comparisons) uses the same order.
- Use [`TRY_CAST`](try_cast.md) when dirty source data should produce `NULL` instead of an error.
- In engine-context queries, `CAST` uses ETL-SQL conversion behavior. Inside `EXECUTE connection BEGIN ... END`, use the target database's native conversion syntax.

## Examples

```sql
SELECT CAST('1' AS INT) AS customer_id;

SELECT CAST('2026-07-16' AS DATE) AS business_date;

SELECT amount_text, CAST(amount_text AS DECIMAL(18, 2)) AS amount
FROM #staging
WHERE amount_text IS NOT NULL;
```

## References

- [Functions](../README.md)
- [TRY_CAST](try_cast.md)
- [CONVERT](convert.md)
