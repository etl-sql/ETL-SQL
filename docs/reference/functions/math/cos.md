# COS

Returns the trigonometric cosine of an angle in radians.

## Syntax

```sql
COS(radians)
```

## Parameters

- **radians** - Angle in radians.

## Returns

Returns a `FLOAT` cosine value in the range `-1.0` through `1.0`.

## Null Behavior

Returns `NULL` when `radians` is `NULL`.

## Examples

```sql
SELECT COS(0) AS cosine_value;
```

```sql
SELECT angle_radians, COS(angle_radians) AS cosine_value
FROM #angles;
```

## References

- [Standard Library](../standard-library.md)
- [SIN](sin.md)
- [TAN](tan.md)
- [ACOS](acos.md)
