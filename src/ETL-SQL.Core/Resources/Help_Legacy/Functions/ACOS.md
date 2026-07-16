# ACOS
Returns the arccosine (inverse cosine) of a number, in radians.

**Category:** Math

## Syntax
```sql
ACOS(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` | Value in [-1.0, 1.0] |

## Returns
`FLOAT` — Angle in radians in the range [0, π].

## Example
```sql
SELECT ACOS(1.0);     -- → 0.0
SELECT ACOS(-1.0);    -- → 3.14159...  (π)
```

## See Also
- [Standard Library — §5.2 Trigonometry](../../../../../Docs/Reference/Standard_Library.md#52-trigonometry-inputoutput-in-radians)
- Related: [`COS`](COS.md), [`ASIN`](ASIN.md), [`ATAN`](ATAN.md)
