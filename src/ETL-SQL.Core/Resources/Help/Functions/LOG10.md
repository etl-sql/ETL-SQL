# LOG10
Returns the base-10 logarithm of a number.

**Category:** Math

## Syntax
```sql
LOG10(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `FLOAT` | A positive numeric value |

## Returns
`FLOAT` — log₁₀(number).

## Example
```sql
SELECT LOG10(100);     -- → 2.0
SELECT LOG10(1000);    -- → 3.0
SELECT LOG10(amount) AS log_scale FROM #metrics WHERE amount > 0;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`LOG`](LOG.md), [`EXP`](EXP.md)
