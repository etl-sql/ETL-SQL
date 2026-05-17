# GREATEST
Returns the largest value from a list of arguments.

**Category:** Math

## Syntax
```sql
GREATEST(value1, value2, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value1` | `ANY` | First comparable value |
| `value2` | `ANY` | Second comparable value |
| `...` | `ANY` | Additional values (variadic) |

## Returns
Same type as inputs — the maximum value among all arguments. Returns `NULL` if any argument is `NULL`.

## Example
```sql
SELECT GREATEST(3, 1, 4, 1, 5);           -- → 5
SELECT GREATEST(cost, minimum_charge) AS billed FROM #jobs;
SELECT GREATEST(start_date, '2026-01-01') AS effective_start FROM #projects;
```

## See Also
- [Standard Library — §5.1 Arithmetic](../../../../../Docs/Reference/Standard_Library.md#51-arithmetic)
- Related: [`LEAST`](LEAST.md), [`MAX`](MAX.md)
