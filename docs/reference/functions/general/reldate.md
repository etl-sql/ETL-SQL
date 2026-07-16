# RELDATE

Resolves a relative date expression string into a standard DATETIME value.

## Syntax

```sql
RELDATE(expression)
```

## Parameters

- **expression** - Relative date expression such as `D`, `D-1`, `W-1`, or `M-1`.

## Returns

Returns the resolved `DATETIME`.

## Null Behavior

Returns `NULL` when `expression` is `NULL` or invalid.

## Examples

```sql
SELECT RELDATE('D-7') AS seven_days_ago;
```

```sql
SELECT *
FROM #orders
WHERE order_date >= RELDATE('M-1');
```

## References

- [Standard Library](../standard-library.md)
- [GETDATE](../datetime/getdate.md)
- [NOW](../datetime/now.md)
