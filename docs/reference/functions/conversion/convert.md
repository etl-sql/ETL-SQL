# CONVERT

Converts an expression to a target ETL-SQL data type, with an optional style code for formatted date and string conversions.

## Syntax

```sql
CONVERT(type, expression [, style])
```

## Parameters

- **type** - Target ETL-SQL data type, such as `INT`, `DECIMAL(18,2)`, `VARCHAR(50)`, `DATE`, or `DATETIME`.
- **expression** - Value or expression to convert.
- **style** - Optional style code. Commonly used for date and timestamp strings, such as `112` for `yyyyMMdd`.

## Returns

Returns the converted value using the requested target type.

## Null Behavior

`CONVERT(type, NULL)` returns `NULL`. Non-null values that cannot be converted raise an execution error.

## Remarks

- `CONVERT` is the function-style equivalent of [`CAST`](cast.md) and is useful when a style code is needed.
- Use [`TRY_CONVERT`](../general/try_convert.md) when invalid values should return `NULL` instead of failing the script.
- Inside `EXECUTE connection BEGIN ... END`, use the target database's native `CONVERT` behavior.

## Examples

```sql
SELECT CONVERT(INT, '42') AS customer_id;

SELECT CONVERT(DATE, '20250101', 112) AS business_date;

SELECT CONVERT(DECIMAL(18, 2), amount_text) AS amount
FROM #staging;
```

## References

- [Standard Library](../standard-library.md)
- [CAST](cast.md)
- [TRY_CONVERT](../general/try_convert.md)
