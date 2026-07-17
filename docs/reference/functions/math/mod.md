# MOD

Returns the remainder of integer division.

## Syntax

```sql
MOD(dividend, divisor)
dividend % divisor
```

## Parameters

- **dividend** - Number to divide.
- **divisor** - Divisor.

## Returns

Returns the remainder after dividing `dividend` by `divisor`.

## Null Behavior

Returns `NULL` when any required argument is `NULL` or `divisor` is `0`.

## Examples

```sql
SELECT MOD(10, 3) AS remainder;
```

```sql
SELECT id, MOD(id, 2) AS parity
FROM #items;
```

## References

- [Functions](../README.md)
- [QUOTIENT](quotient.md)
