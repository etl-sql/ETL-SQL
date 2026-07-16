# EXP

Returns e (Euler's number) raised to the specified power.

## Syntax

```sql
EXP(number)
```

## Parameters

- **number** - Exponent to apply to `e`.

## Returns

Returns a `FLOAT` value equal to `e` raised to `number`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT EXP(1) AS e_value;
```

```sql
SELECT value_id, EXP(LOG(x)) AS original_value
FROM #values
WHERE x > 0;
```

## References

- [Standard Library](../standard-library.md)
- [LOG](log.md)
- [POWER](power.md)
