# SQRT
Returns the square root of a non-negative number.

**Category:** Math

## Syntax
```sql
SQRT(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `DECIMAL` / `FLOAT` | A non-negative value |

## Returns
`FLOAT` — The square root of `number`. Raises an error if `number` is negative.

## Example
```sql
SELECT SQRT(9);      -- → 3.0
SELECT SQRT(2);      -- → 1.41421356...
SELECT SQRT(variance) AS std_dev FROM #stats;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`POWER`](power.md), [`EXP`](exp.md)
