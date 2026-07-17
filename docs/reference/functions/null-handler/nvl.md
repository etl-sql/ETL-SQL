# NVL

Returns a replacement when the first argument is NULL. Oracle-style alias for ISNULL.

## Syntax

```sql
NVL(value, replacement)
```

## Parameters

- **value** - Expression to test.
- **replacement** - Value returned when `value` is `NULL`.

## Returns

Returns `value` when it is not `NULL`; otherwise returns `replacement`.

## Null Behavior

Returns `replacement` when `value` is `NULL`.

## Examples

```sql
SELECT NVL(region, 'Unknown') AS region
FROM #data;
```

```sql
SELECT NVL(discount, 0) AS discount
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [ISNULL](../null-handler/isnull.md)
- [NVL2](nvl2.md)
- [COALESCE](../null-handler/coalesce.md)
