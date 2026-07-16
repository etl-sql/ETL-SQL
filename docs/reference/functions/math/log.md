# LOG

Returns the natural logarithm (base e) of a number.

## Syntax

```sql
LOG(number)
LN(number)
```

## Parameters

- **number** - Positive numeric value.

## Returns

Returns a `FLOAT` natural logarithm. `LN` is an alias for `LOG`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Remarks

Use [`LOG10`](log10.md) when you need base-10 logarithms.

## Examples

```sql
SELECT LOG(EXP(1)) AS natural_log_value;
```

```sql
SELECT metric_id, LOG(amount) AS log_amount
FROM #metrics
WHERE amount > 0;
```

## References

- [Standard Library](../standard-library.md)
- [LOG10](log10.md)
- [EXP](exp.md)
- [POWER](power.md)
