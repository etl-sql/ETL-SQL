# NULLIF

Returns NULL if two expressions are equal; otherwise returns the first expression.

## Syntax

```sql
NULLIF(value1, value2)
```

## Parameters

- **value1** - Value returned when the two values are not equal.
- **value2** - Comparison value.

## Returns

Returns `NULL` when `value1 = value2`; otherwise returns `value1`.

## Null Behavior

Returns `NULL` when the two values compare equal. If they do not compare equal, the result follows `value1`.

## Remarks

- Classic use: avoid division-by-zero: `value / NULLIF(denominator, 0)`.
- Also used with `COALESCE` to treat empty strings as NULL: `COALESCE(NULLIF(TRIM(col), ''), 'default')`.

## Examples

```sql
SELECT NULLIF(10, 10) AS equal_result;
```

```sql
SELECT order_id, total / NULLIF(qty, 0) AS unit_price
FROM #orders;
```

```sql
SELECT COALESCE(NULLIF(TRIM(region), ''), 'Unknown') AS normalized_region
FROM #data;
```

## References

- [Functions](../README.md)
- [COALESCE](coalesce.md)
- [ISNULL](isnull.md)
