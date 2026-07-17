# CEIL

Rounds a numeric value up to the nearest integer. `CEIL` is an alias for [`CEILING`](ceiling.md).

## Syntax

```sql
CEIL(number)
```

## Parameters

- **number** - Numeric expression to round upward.

## Returns

Returns the smallest integer value greater than or equal to `number`.

## Null Behavior

`CEIL(NULL)` returns `NULL`.

## Remarks

- `CEIL(123.01)` returns `124`.
- `CEIL(-123.01)` returns `-123`.
- Use [`FLOOR`](floor.md) to round downward.

## Examples

```sql
SELECT CEIL(123.01) AS rounded_up;
```

```sql
SELECT CEIL(total_rows / 1000.0) AS page_count
FROM #batch_summary;
```

## References

- [Functions](../README.md)
- [CEILING](ceiling.md)
- [FLOOR](floor.md)
