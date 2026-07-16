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
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`SIGN`](SIGN.md), [`ROUND`](ROUND.md)
