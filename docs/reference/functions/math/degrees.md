# DEGREES
Converts an angle value in radians to degrees.

**Category:** Math

## Syntax
```sql
DEGREES(radians)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `radians` | `DECIMAL` / `FLOAT` | The angle in radians |

## Returns
`DECIMAL` — The angle in degrees. Returns `NULL` if input is `NULL`.

## Example
```sql
SELECT DEGREES(PI());   -- → 180
```

## See Also
- [Standard Library — §5.2 Trigonometric](../../../guides/getting-started.md#52-trigonometric)
- Related: [`RADIANS`](radians.md), [`PI`](pi.md)
