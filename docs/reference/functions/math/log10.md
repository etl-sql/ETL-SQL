# LOG10

Returns the base-10 logarithm of a number.

## Syntax

```sql
LOG10(number)
```

## Parameters

- **number** - Positive numeric value.

## Returns

Returns a `FLOAT` base-10 logarithm.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT LOG10(1000) AS log10_value;
```

```sql
SELECT metric_id, LOG10(amount) AS log_scale
FROM #metrics
WHERE amount > 0;
```

## References

- [Functions](../README.md)
- [LOG](log.md)
- [EXP](exp.md)
