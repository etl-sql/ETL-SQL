# NVL2
Returns one value if the expression is NOT NULL, and another if it IS NULL. Oracle-style conditional.

**Category:** Logic

## Syntax
```sql
NVL2(value, not_null_result, null_result)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value` | `ANY` | The expression to test for NULL |
| `not_null_result` | `ANY` | Returned when `value` is NOT NULL |
| `null_result` | `ANY` | Returned when `value` IS NULL |

## Returns
Same type as `not_null_result` / `null_result`.

## Example
```sql
SELECT NVL2(phone, 'Has phone', 'No phone') FROM #contacts;
SELECT NVL2(discount, price * (1 - discount), price) AS final_price FROM #items;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../guides/getting-started.md#7-conditional--null-handling-functions)
- Related: [`ISNULL`](../conversion/isnull.md), [`IIF`](../conversion/iif.md), [`COALESCE`](../conversion/coalesce.md)
