# CEILING
Returns the smallest integer greater than or equal to a number.

**Category:** Math

## Syntax
```sql
CEILING(number)
CEIL(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `DECIMAL` / `FLOAT` | The value to ceiling |

## Returns
`INT` — The smallest integer ≥ `number`. `CEIL` is an alias for `CEILING`.

## Example
```sql
SELECT CEILING(3.1);    -- → 4
SELECT CEILING(-3.9);   -- → -3
SELECT CEIL(3.0);       -- → 3
SELECT CEILING(qty / 10.0) * 10 AS next_pack_size FROM #orders;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`FLOOR`](floor.md), [`ROUND`](round.md)
