# COT
Returns the cotangent of the angle specified in radians.

**Category:** Math

## Syntax
```sql
COT(radians)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `radians` | `DECIMAL` / `FLOAT` | The angle in radians |

## Returns
`DECIMAL` — The cotangent value. Returns `NULL` if input is `NULL` or results in division by zero (e.g. at 0 radians).

## Example
```sql
SELECT COT(0.5);        -- → 1.830487721712452
```

## See Also
- [Standard Library — §5.2 Trigonometric](../../../../../Docs/Reference/Standard_Library.md#52-trigonometric)
- Related: [`TAN`](TAN.md), [`SIN`](SIN.md), [`COS`](COS.md)
