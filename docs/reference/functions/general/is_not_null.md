# IS_NOT_NULL

Returns whether an expression is not `NULL`.

## Syntax

```sql
IS_NOT_NULL(expression)
```

## Parameters

- **expression** - Value or expression to test.

## Returns

Returns `1` when the expression is not `NULL`; otherwise returns `0`.

## Null Behavior

`IS_NOT_NULL(NULL)` returns `0`.

## Remarks

- `IS_NOT_NULL(expression)` is useful in generated expressions and filters.
- In ordinary predicates, `expression IS NOT NULL` is usually clearer.
- Use [`IS_NULL`](is_null.md) for the inverse test.

## Examples

```sql
SELECT *
FROM #customers
WHERE IS_NOT_NULL(email) = 1;
```

```sql
SELECT order_id
FROM #orders
WHERE IS_NOT_NULL(shipped_at) = 1;
```

## References

- [Standard Library](../standard-library.md)
- [IS_NULL](is_null.md)
