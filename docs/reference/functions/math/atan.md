# ATAN

Returns the arctangent (inverse tangent) of a number, in radians.

## Syntax

```sql
ATAN(number)
ATAN2(y, x)
```

## Parameters

- **number** - Input value for `ATAN`.
- **y** - Y-coordinate for `ATAN2`.
- **x** - X-coordinate for `ATAN2`.

## Returns

Returns a `FLOAT` angle in radians. `ATAN` returns values from `-PI()/2` through `PI()/2`; `ATAN2` returns a quadrant-aware angle from `-PI()` through `PI()`.

## Null Behavior

Returns `NULL` when any required argument is `NULL`.

## Remarks

- `ATAN2(y, x)` is preferred over `ATAN(y/x)` because it handles all quadrants and avoids division-by-zero.

## Examples

```sql
SELECT ATAN(1.0) AS angle_radians;
```

```sql
SELECT ATAN2(delta_y, delta_x) AS direction_radians
FROM #vectors;
```

## References

- [Functions](../README.md)
- [TAN](tan.md)
- [SIN](sin.md)
- [COS](cos.md)
