# RADIANS
Converts an angle value in degrees to radians.

**Category:** Math

## Syntax
```sql
RADIANS(degrees)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `degrees` | `DECIMAL` / `FLOAT` | The angle in degrees |

## Returns
`DECIMAL` — The angle in radians. Returns `NULL` if input is `NULL`.

## Example
```sql
SELECT RADIANS(180);    -- → 3.141592653589793 (PI)
```

## See Also
- [Standard Library — §5.2 Trigonometric](../../../guides/getting-started.md#52-trigonometric)
- Related: [`DEGREES`](degrees.md), [`PI`](pi.md)
