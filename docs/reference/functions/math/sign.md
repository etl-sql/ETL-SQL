# SIGN

Returns the sign of a number: 1 (positive), -1 (negative), or 0 (zero).

## Syntax

```sql
SIGN(number)
```

## Parameters

- **number** - Numeric value to evaluate.

## Returns

Returns `-1`, `0`, or `1`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT SIGN(-42) AS direction;
```

```sql
SELECT transaction_id, SIGN(balance_delta) AS balance_direction
FROM #transactions;
```

## References

- [Standard Library](../standard-library.md)
- [ABS](abs.md)
