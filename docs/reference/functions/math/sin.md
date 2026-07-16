# SIN
Returns the trigonometric sine of an angle in radians.

**Category:** Math

## Syntax
```sql
SIN(radians)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `radians` | `FLOAT` | Angle in radians |

## Returns
`FLOAT` — Sine of the angle, in the range [-1.0, 1.0].

## Remarks
- To convert degrees to radians: `degrees * (PI() / 180.0)`.

## Example
```sql
SELECT SIN(0);                              -- → 0.0
SELECT SIN(PI() / 2);                       -- → 1.0
SELECT SIN(45 * (PI() / 180.0));            -- → 0.7071...
```

## See Also
- [Standard Library — §5.2 Trigonometry](../../../guides/getting-started.md#52-trigonometry-inputoutput-in-radians)
- Related: [`COS`](cos.md), [`TAN`](tan.md), [`ASIN`](asin.md), [`PI`](pi.md)
