# TRY_CAST

Safely converts a value to the specified type, returning NULL on failure instead of raising an error.

## Syntax

```sql
TRY_CAST(expression AS type)
```

## Parameters

- **expression** - Value to convert.
- **type** - Target data type.

## Returns

Returns the converted value using the requested target type, or `NULL` when conversion fails.

## Null Behavior

Returns `NULL` when `expression` is `NULL` or cannot be converted to `type`.

## Remarks

- Use `TRY_CAST` when source data may contain non-convertible values (dirty data).
- `CAST` is the strict form; use it when invalid data should raise an error.

## Examples

```sql
SELECT TRY_CAST('42' AS INT) AS parsed_value;
```

```sql
SELECT raw_amount
FROM #raw_orders
WHERE TRY_CAST(raw_amount AS DECIMAL(18, 2)) IS NULL;
```

```sql
SELECT *
INTO #valid_orders
FROM #raw_orders
WHERE TRY_CAST(raw_amount AS DECIMAL(18, 2)) IS NOT NULL;
```

## References

- [Functions](../README.md)
- [CAST](cast.md)
- [TRY_CONVERT](../conversion/try_convert.md)
- [TRY_PARSE](../conversion/try_parse.md)
- [ISDATE](../datetime/isdate.md)
