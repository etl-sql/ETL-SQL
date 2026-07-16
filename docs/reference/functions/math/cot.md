# COT

Returns the cotangent of the angle specified in radians.

## Syntax

```sql
COT(radians)
```

## Parameters

- **radians** - Angle in radians.

## Returns

Returns the cotangent value.

## Null Behavior

Returns `NULL` when `radians` is `NULL` or when the calculation would divide by zero.

## Examples

```sql
SELECT COT(0.5) AS cotangent_value;
```

```sql
SELECT angle_radians, COT(angle_radians) AS cotangent_value
FROM #angles
WHERE angle_radians <> 0;
```

## References

- [Standard Library](../standard-library.md)
- [TAN](tan.md)
- [SIN](sin.md)
- [COS](cos.md)
