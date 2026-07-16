# TAN

Returns the trigonometric tangent of an angle in radians.

## Syntax

```sql
TAN(radians)
```

## Parameters

- **radians** - Angle in radians.

## Returns

Returns a `FLOAT` tangent value.

## Null Behavior

Returns `NULL` when `radians` is `NULL`.

## Remarks

The tangent is undefined at `PI()/2 + n * PI()`.

## Examples

```sql
SELECT TAN(0) AS tangent_value;
```

```sql
SELECT angle_radians, TAN(angle_radians) AS tangent_value
FROM #angles;
```

## References

- [Standard Library](../standard-library.md)
- [SIN](sin.md)
- [COS](cos.md)
- [ATAN](atan.md)
