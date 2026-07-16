# POWER
Raises a base number to an exponent.

**Category:** Math

## Syntax
```sql
POWER(base, exponent)
POW(base, exponent)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `base` | `DECIMAL` / `FLOAT` | The base number |
| `exponent` | `DECIMAL` / `FLOAT` | The exponent |

## Returns
`FLOAT` — `base` raised to the power of `exponent`. `POW` is an alias for `POWER`.

## Example
```sql
SELECT POWER(2, 10);     -- → 1024
SELECT POWER(9, 0.5);    -- → 3.0  (square root)
SELECT POW(10, 3);       -- → 1000
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`SQRT`](SQRT.md), [`EXP`](EXP.md), [`LOG`](LOG.md)
