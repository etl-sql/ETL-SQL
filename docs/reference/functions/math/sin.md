# SIN

Returns the trigonometric sine of an angle in radians.

## Syntax

```sql
SIN(radians)
```

## Parameters

- **radians** - Angle in radians.

## Returns

Returns a `FLOAT` sine value from `-1.0` through `1.0`.

## Null Behavior

Returns `NULL` when `radians` is `NULL`.

## Remarks

- To convert degrees to radians: `degrees * (PI() / 180.0)`.

## Examples

```sql
SELECT SIN(0) AS sine_zero;
```

```sql
SELECT SIN(angle_degrees * (PI() / 180.0)) AS sine_value
FROM #angles;
```

## References

- [Functions](../README.md)
- [COS](cos.md)
- [TAN](tan.md)
- [ASIN](asin.md)
- [PI](pi.md)
