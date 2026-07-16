# ABS
Returns the absolute (non-negative) value of a number.

**Category:** Math

## Syntax
```sql
ABS(number)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `number` | `INT` / `DECIMAL` / `FLOAT` | The numeric value |

## Returns
Same type as input — the non-negative magnitude of `number`.

## Example
```sql
SELECT ABS(-42);        -- → 42
SELECT ABS(42);         -- → 42
SELECT ABS(balance) AS abs_balance FROM #accounts;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`SIGN`](sign.md), [`ROUND`](round.md)
