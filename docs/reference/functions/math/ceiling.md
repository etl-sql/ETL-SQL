# CEILING

Returns the smallest integer greater than or equal to a number.

## Syntax

```sql
CEILING(number)
CEIL(number)
```

## Parameters

- **number** - Numeric value to round upward.

## Returns

Returns the smallest integer greater than or equal to `number`. `CEIL` is an alias for `CEILING`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT CEILING(3.1) AS rounded_up;
```

```sql
SELECT CEILING(qty / 10.0) * 10 AS next_pack_size
FROM #orders;
```

## References

- [Functions](../README.md)
- [CEIL](ceil.md)
- [FLOOR](floor.md)
- [ROUND](round.md)
