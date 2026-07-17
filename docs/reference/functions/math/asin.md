# ASIN

Returns the arcsine (inverse sine) of a number, in radians.

## Syntax

```sql
ASIN(number)
```

## Parameters

- **number** - Numeric value in the range `-1.0` through `1.0`.

## Returns

Returns a `FLOAT` angle in radians in the range `-PI()/2` through `PI()/2`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT ASIN(1.0) AS angle_radians;
```

```sql
SELECT vector_id, ASIN(sine_value) AS angle_radians
FROM #vectors;
```

## References

- [Functions](../README.md)
- [SIN](sin.md)
- [ACOS](acos.md)
- [ATAN](atan.md)
