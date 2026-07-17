# SQRT

Returns the square root of a non-negative number.

## Syntax

```sql
SQRT(number)
```

## Parameters

- **number** - Non-negative numeric value.

## Returns

Returns a `FLOAT` square root.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Remarks

Negative inputs raise an error.

## Examples

```sql
SELECT SQRT(9) AS root_value;
```

```sql
SELECT metric_id, SQRT(variance) AS std_dev
FROM #stats
WHERE variance >= 0;
```

## References

- [Functions](../README.md)
- [POWER](power.md)
- [EXP](exp.md)
