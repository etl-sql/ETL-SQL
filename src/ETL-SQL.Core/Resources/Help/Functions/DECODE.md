# DECODE
Evaluates a value against a series of search terms, returning the corresponding result. Oracle-style CASE shorthand.

**Category:** Logic

## Syntax
```sql
DECODE(value, search1, result1, search2, result2, ..., [default])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value` | `ANY` | The expression to compare |
| `searchN` | `ANY` | Comparison values (matched in order) |
| `resultN` | `ANY` | Value returned when `value = searchN` |
| `default` | `ANY` | Optional: value returned if no search matches |

## Returns
Same type as `resultN` — the first matching result, or `default` (NULL if omitted).

## Example
```sql
SELECT DECODE(status, 'A', 'Active', 'I', 'Inactive', 'Unknown') FROM #customers;
SELECT DECODE(MONTH(order_date), 12, 'Q4', 11, 'Q4', 10, 'Q4', 'Other') AS quarter
  FROM #orders;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../../../Docs/Reference/Standard_Library.md#7-conditional--null-handling-functions)
- Related: [`IIF`](IIF.md), [`COALESCE`](COALESCE.md)
