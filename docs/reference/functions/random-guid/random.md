# RANDOM

Returns a pseudo-random decimal value greater than or equal to `0` and less than `1`.

## Syntax

```sql
RANDOM()
```

## Parameters

None.

## Returns

Returns a decimal value in the range `[0, 1)`.

## Null Behavior

`RANDOM()` does not take arguments and never returns `NULL`.

## Remarks

- `RANDOM()` is non-deterministic and may return a different value for each evaluation.
- Use [`RANDOM_INT`](random_int.md) for whole-number ranges.
- Use [`RANDOM_DECIMAL`](random_decimal.md) for explicit decimal ranges.

## Examples

```sql
SELECT RANDOM() AS sample_value;
```

```sql
SELECT *
FROM #customers
WHERE RANDOM() < 0.10;
```

## References

- [Functions](../README.md)
- [RANDOM_INT](random_int.md)
- [RANDOM_DECIMAL](random_decimal.md)
