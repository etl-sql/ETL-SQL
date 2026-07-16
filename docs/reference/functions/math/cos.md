# COS
Returns the trigonometric cosine of an angle in radians.

**Category:** Math

## Syntax
```sql
COS(radians)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `radians` | `FLOAT` | Angle in radians |

## Returns
`FLOAT` — Cosine of the angle, in the range [-1.0, 1.0].

## Example
```sql
SELECT COS(0);           -- → 1.0
SELECT COS(PI());        -- → -1.0
```

## See Also
- [Standard Library — §5.2 Trigonometry](../../../guides/getting-started.md#52-trigonometry-inputoutput-in-radians)
- Related: [`SIN`](sin.md), [`TAN`](tan.md), [`ACOS`](acos.md)
