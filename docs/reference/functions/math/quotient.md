# QUOTIENT

Returns the integer quotient of a division operation.

## Syntax

```sql
QUOTIENT(dividend, divisor)
```

## Parameters

- **dividend** - Numeric value to divide.
- **divisor** - Numeric value to divide by.

## Returns

Returns the integer quotient after division.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Errors

- Division by zero raises an execution error.

## Remarks

- Use [`MOD`](mod.md) when you need the remainder.
- Use normal division (`/`) when fractional results should be preserved.

## Examples

```sql
SELECT QUOTIENT(10, 3) AS whole_units;
```

```sql
SELECT
  total_rows,
  QUOTIENT(total_rows, 1000) AS full_batches,
  MOD(total_rows, 1000) AS remainder_rows
FROM #load_summary;
```

## References

- [Functions](../README.md)
- [MOD](mod.md)
