# TRUNCATE
Truncates a number to a specified number of decimal places without rounding.

**Category:** Math

## Syntax
```sql
TRUNCATE(number, decimals)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `DECIMAL` / `FLOAT` | The value to truncate |
| `decimals` | `INT` | Number of decimal places to keep |

## Returns
`DECIMAL` — The value truncated toward zero (no rounding).

## Example
```sql
SELECT TRUNCATE(3.999, 2);    -- → 3.99  (not 4.00)
SELECT TRUNCATE(3.999, 0);    -- → 3.0
SELECT TRUNCATE(-3.999, 1);   -- → -3.9
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`FLOOR`](FLOOR.md), [`ROUND`](ROUND.md)
