# LOG
Returns the natural logarithm (base e) of a number.

**Category:** Math

## Syntax
```sql
LOG(number)
LN(number)
LOG10(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` | A positive numeric value |

## Returns
`FLOAT` — The natural log of `number`. `LN` is an alias. `LOG10` returns the base-10 logarithm.

## Example
```sql
SELECT LOG(1);        -- → 0.0
SELECT LOG(EXP(1));   -- → 1.0
SELECT LN(100);       -- → 4.60517...
SELECT LOG10(1000);   -- → 3.0
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`EXP`](EXP.md), [`POWER`](POWER.md)
