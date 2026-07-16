# ASIN
Returns the arcsine (inverse sine) of a number, in radians.

**Category:** Math

## Syntax
```sql
ASIN(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` | Value in [-1.0, 1.0] |

## Returns
`FLOAT` — Angle in radians in the range [-π/2, π/2].

## Example
```sql
SELECT ASIN(1.0);     -- → 1.5708...  (π/2)
SELECT ASIN(0.0);     -- → 0.0
```

## See Also
- [Standard Library — §5.2 Trigonometry](../../../guides/getting-started.md#52-trigonometry-inputoutput-in-radians)
- Related: [`SIN`](sin.md), [`ACOS`](acos.md), [`ATAN`](atan.md)
