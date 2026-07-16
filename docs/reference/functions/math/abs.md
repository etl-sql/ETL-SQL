# ABS

Returns the absolute (non-negative) value of a number.

## Syntax

```sql
ABS(number)
```

## Parameters

- **number** - Numeric value to evaluate.

## Returns

Returns the non-negative magnitude of `number`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT ABS(-42) AS magnitude;
```

```sql
SELECT account_id, ABS(balance) AS abs_balance
FROM #accounts;
```

## References

- [Standard Library](../standard-library.md)
- [SIGN](sign.md)
- [ROUND](round.md)
