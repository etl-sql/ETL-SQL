# FLOOR
Returns the largest integer less than or equal to a number.

**Category:** Math

## Syntax
```sql
FLOOR(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `DECIMAL` / `FLOAT` | The value to floor |

## Returns
`INT` — The largest integer ≤ `number`.

## Example
```sql
SELECT FLOOR(3.9);     -- → 3
SELECT FLOOR(-3.1);    -- → -4
SELECT FLOOR(price) AS floor_price FROM #catalog;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`CEILING`](CEILING.md), [`ROUND`](ROUND.md), [`TRUNCATE`](TRUNCATE.md)
