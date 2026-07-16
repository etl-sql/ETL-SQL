# TAN
Returns the trigonometric tangent of an angle in radians.

**Category:** Math

## Syntax
```sql
TAN(radians)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `radians` | `FLOAT` | Angle in radians. Undefined at π/2 + nπ |

## Returns
`FLOAT` — Tangent of the angle.

## Example
```sql
SELECT TAN(0);                    -- → 0.0
SELECT TAN(PI() / 4);             -- → 1.0  (45 degrees)
```

## See Also
- [Standard Library — §5.2 Trigonometry](../../../guides/getting-started.md#52-trigonometry-inputoutput-in-radians)
- Related: [`SIN`](sin.md), [`COS`](cos.md), [`ATAN`](atan.md)
