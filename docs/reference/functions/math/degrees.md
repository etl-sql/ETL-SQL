# DEGREES

Converts an angle value in radians to degrees.

## Syntax

```sql
DEGREES(radians)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `radians` | `DECIMAL` / `FLOAT` | The angle in radians |

## Returns

Returns a numeric angle in degrees.

## Null Behavior

`DEGREES(NULL)` returns `NULL`.

## Examples

```sql
SELECT DEGREES(PI()) AS half_turn_degrees;
```

```sql
SELECT DEGREES(angle_radians) AS angle_degrees
FROM #vectors;
```

## References

- [Functions](../README.md)
- [RADIANS](radians.md)
- [PI](pi.md)
