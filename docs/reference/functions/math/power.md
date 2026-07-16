# POWER

Raises a base number to an exponent.

## Syntax

```sql
POWER(base, exponent)
POW(base, exponent)
```

## Parameters

- **base** - Base numeric value.
- **exponent** - Exponent to apply to `base`.

## Returns

Returns `base` raised to the power of `exponent` as a numeric value. `POW` is an alias for `POWER`.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Examples

```sql
SELECT POWER(2, 10) AS two_to_ten;
```

```sql
SELECT POWER(amount, 2) AS amount_squared
FROM #values;
```

## References

- [Standard Library](../standard-library.md)
- [SQRT](sqrt.md)
- [EXP](exp.md)
- [LOG](log.md)
