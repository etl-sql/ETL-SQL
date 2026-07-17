# IS_NULL

Returns whether an expression evaluates to `NULL`.

## Syntax

```sql
IS_NULL(expression)
```

## Parameters

- **expression** - Value or expression to test.

## Returns

Returns `1` when the expression is `NULL`; otherwise returns `0`.

## Null Behavior

`IS_NULL(NULL)` returns `1`.

## Remarks

- `IS_NULL(expression)` is useful in generated expressions and filters.
- In ordinary predicates, `expression IS NULL` is usually clearer.
- Use [`IS_NOT_NULL`](is_not_null.md) for the inverse test.

## Examples

```sql
SELECT *
FROM #customers
WHERE IS_NULL(email) = 1;
```

```sql
UPDATE #stage
SET quality_issue = 'missing account id'
WHERE IS_NULL(account_id) = 1;
```

## References

- [Functions](../README.md)
- [IS_NOT_NULL](is_not_null.md)
