# RADIANS

Converts an angle value in degrees to radians.

## Syntax

```sql
RADIANS(degrees)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `degrees` | `DECIMAL` / `FLOAT` | The angle in degrees |

## Returns

Returns a numeric angle in radians.

## Null Behavior

`RADIANS(NULL)` returns `NULL`.

## Examples

```sql
SELECT RADIANS(180) AS half_turn_radians;
```

```sql
SELECT RADIANS(angle_degrees) AS angle_radians
FROM #vectors;
```

## References

- [Standard Library](../standard-library.md)
- [DEGREES](degrees.md)
- [PI](pi.md)
