# ATAN2

Returns the angle, in radians, between the positive x-axis and the point represented by `(x, y)`.

## Syntax

```sql
ATAN2(y, x)
```

## Parameters

- **y** - Y coordinate or vertical component.
- **x** - X coordinate or horizontal component.

## Returns

Returns a numeric angle in radians.

## Null Behavior

Returns `NULL` when `x` or `y` is `NULL`.

## Remarks

- `ATAN2` uses both coordinates to determine the correct quadrant.
- Use [`DEGREES`](degrees.md) to convert the result to degrees.
- Use [`ATAN`](atan.md) when you only have a single tangent value.

## Examples

```sql
SELECT ATAN2(1, 1) AS radians;
```

```sql
SELECT DEGREES(ATAN2(delta_y, delta_x)) AS heading_degrees
FROM #vectors;
```

## References

- [Standard Library](../standard-library.md)
- [ATAN](atan.md)
- [DEGREES](degrees.md)
