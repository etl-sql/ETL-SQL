# ATAN
Returns the arctangent (inverse tangent) of a number, in radians.

**Category:** Math

## Syntax
```sql
ATAN(number)
ATAN2(y, x)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` | Any real number |
| `y` | `FLOAT` | Y-coordinate (for ATAN2) |
| `x` | `FLOAT` | X-coordinate (for ATAN2) |

## Returns
`FLOAT` — `ATAN`: angle in radians in [-π/2, π/2]. `ATAN2`: quadrant-aware angle in (-π, π].

## Remarks
- `ATAN2(y, x)` is preferred over `ATAN(y/x)` because it handles all quadrants and avoids division-by-zero.

## Example
```sql
SELECT ATAN(1.0);           -- → 0.7854...  (π/4)
SELECT ATAN2(1.0, 1.0);     -- → 0.7854...  (45° angle)
SELECT ATAN2(0.0, -1.0);    -- → 3.14159... (π, pointing left)
```

## See Also
- [Standard Library — §5.2 Trigonometry](../../../../../Docs/Reference/Standard_Library.md#52-trigonometry-inputoutput-in-radians)
- Related: [`TAN`](TAN.md), [`SIN`](SIN.md), [`COS`](COS.md)
