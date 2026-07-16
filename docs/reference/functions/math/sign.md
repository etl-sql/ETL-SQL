# SIGN
Returns the sign of a number: 1 (positive), -1 (negative), or 0 (zero).

**Category:** Math

## Syntax
```sql
SIGN(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `INT` / `DECIMAL` / `FLOAT` | The value to evaluate |

## Returns
`INT` — `-1`, `0`, or `1`.

## Example
```sql
SELECT SIGN(-42);     -- → -1
SELECT SIGN(0);       -- → 0
SELECT SIGN(100);     -- → 1
SELECT SIGN(balance) AS direction FROM #transactions;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`ABS`](abs.md)
