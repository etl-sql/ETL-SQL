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
- [Standard Library — §5.2 Trigonometric](../../../guides/getting-started.md#52-trigonometric)
- Related: [`TAN`](tan.md), [`SIN`](sin.md), [`COS`](cos.md)
