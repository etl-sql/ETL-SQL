# TRY_CONVERT

Converts an expression to a target ETL-SQL data type and returns `NULL` when conversion fails.

## Syntax

```sql
TRY_CONVERT(type, expression [, style])
```

## Parameters

- **type** - Target ETL-SQL data type.
- **expression** - Value or expression to convert.
- **style** - Optional style code for formatted date and string conversions.

## Returns

Returns the converted value using the requested target type, or `NULL` when conversion fails.

## Null Behavior

`TRY_CONVERT(type, NULL)` returns `NULL`.

## Remarks

- Use `TRY_CONVERT` for dirty source data, validation gates, and quarantine decisions.
- Use [`CONVERT`](../conversion/convert.md) when invalid data should stop the script.
- For `CAST` syntax, use [`TRY_CAST`](../conversion/try_cast.md).

## Examples

```sql
SELECT TRY_CONVERT(INT, '42') AS valid_value;
SELECT TRY_CONVERT(INT, 'N/A') AS invalid_value;
```

```sql
SELECT *
INTO #valid_orders
FROM #raw_orders
WHERE TRY_CONVERT(DECIMAL(18, 2), amount_text) IS NOT NULL;
```

```sql
SELECT TRY_CONVERT(DATE, '20250101', 112) AS business_date;
```

## References

- [Standard Library](../standard-library.md)
- [CONVERT](../conversion/convert.md)
- [TRY_CAST](../conversion/try_cast.md)
