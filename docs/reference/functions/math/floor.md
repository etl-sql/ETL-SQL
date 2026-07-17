# FLOOR

Returns the largest integer less than or equal to a number.

## Syntax

```sql
FLOOR(number)
```

## Parameters

- **number** - Numeric value to round down.

## Returns

Returns the largest integer less than or equal to `number`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT FLOOR(3.9) AS rounded_down;
```

```sql
SELECT sku, FLOOR(price) AS floor_price
FROM #catalog;
```

## References

- [Functions](../README.md)
- [CEILING](ceiling.md)
- [ROUND](round.md)
