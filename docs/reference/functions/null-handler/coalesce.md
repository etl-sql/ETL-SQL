# COALESCE

Returns the first non-NULL value from a list of expressions.

## Syntax

```sql
COALESCE(value1, value2, ...)
```

## Parameters

- **value1** - First value to test.
- **value2** - Next fallback value.
- **...** - Additional fallback values.

## Returns

Returns the first non-NULL value in the argument list.

## Null Behavior

Returns `NULL` when every argument is `NULL`.

## Remarks

- Evaluation stops at the first non-NULL argument.
- `COALESCE(a, b, c)` is equivalent to a searched `CASE` expression that tests each value for `IS NOT NULL`.
- **Dialect Shorthand**: ETL-SQL provides the `??` operator as shorthand for `COALESCE`: `a ?? b ?? c` compiles to `COALESCE(a, b, c)` at parse time. See [Expressions and Operators](../../statements/expressions-and-operators.md).

## Examples

```sql
SELECT COALESCE(NULL, NULL, 'fallback') AS selected_value;
```

```sql
-- Using the ?? shorthand equivalent
SELECT user_id, nickname ?? first_name ?? 'Unknown' AS display_name
FROM #users;
```

```sql
SELECT COALESCE(NULLIF(TRIM(region), ''), 'Unknown') AS region
FROM #staging;
```

## References

- [Functions](../README.md)
- [Expressions and Operators](../../statements/expressions-and-operators.md)
- [ISNULL](isnull.md)
- [NULLIF](nullif.md)
- [IIF](../conversion/iif.md)
