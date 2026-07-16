# ACOS

Returns the arccosine (inverse cosine) of a number, in radians.

## Syntax

```sql
ACOS(number)
```

## Parameters

- **number** - Numeric value in the range `-1.0` through `1.0`.

## Returns

Returns a `FLOAT` angle in radians in the range `0` through `PI()`.

## Null Behavior

Returns `NULL` when `number` is `NULL`.

## Examples

```sql
SELECT ACOS(1.0) AS angle_radians;
```

```sql
SELECT vector_id, ACOS(cosine_value) AS angle_radians
FROM #vectors;
```

## References

- [Standard Library](../standard-library.md)
- [COS](cos.md)
- [ASIN](asin.md)
- [ATAN](atan.md)
