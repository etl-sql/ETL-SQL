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
- [Standard Library — §5.2 Trigonometry](../../../../../Docs/Reference/Standard_Library.md#52-trigonometry-inputoutput-in-radians)
- Related: [`SIN`](SIN.md), [`COS`](COS.md), [`ATAN`](ATAN.md)
