# ROUND
Rounds a number to the specified number of decimal places.

**Category:** Math

## Syntax
```sql
ROUND(number, decimals)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `DECIMAL` / `FLOAT` | The value to round |
| `decimals` | `INT` | Decimal places to round to. Negative values round to the left of the decimal point |

## Returns
`DECIMAL` — The rounded value.

## Example
```sql
SELECT ROUND(3.14159, 2);    -- → 3.14
SELECT ROUND(3.145, 2);      -- → 3.15  (rounds half up)
SELECT ROUND(1234.5, -2);    -- → 1200
SELECT ROUND(amount, 2) AS rounded FROM #prices;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`FLOOR`](FLOOR.md), [`CEILING`](CEILING.md), [`TRUNCATE`](TRUNCATE.md)
