# LEAST
Returns the smallest value from a list of arguments.

**Category:** Math

## Syntax
```sql
LEAST(value1, value2, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value1` | `ANY` | First comparable value |
| `value2` | `ANY` | Second comparable value |
| `...` | `ANY` | Additional values (variadic) |

## Returns
Same type as inputs — the minimum value among all arguments. Returns `NULL` if any argument is `NULL`.

## Example
```sql
SELECT LEAST(3, 1, 4, 1, 5);              -- → 1
SELECT LEAST(sale_price, list_price) AS effective_price FROM #items;
SELECT LEAST(deadline, GETDATE() + 7) AS effective_deadline FROM #tasks;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../guides/getting-started.md#51-arithmetic)
- Related: [`GREATEST`](greatest.md), [`MIN`](../aggregate/min.md)
