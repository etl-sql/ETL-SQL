# IIF
Returns one of two values based on a boolean condition. Inline conditional expression.

**Category:** Logic

## Syntax
```sql
IIF(condition, true_value, false_value)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `condition` | `BIT` / `BOOLEAN` | The condition to evaluate |
| `true_value` | `ANY` | Value returned when `condition` is TRUE |
| `false_value` | `ANY` | Value returned when `condition` is FALSE or NULL |

## Returns
Same type as `true_value` / `false_value` — the appropriate branch value.

## Remarks
- Equivalent to `CASE WHEN condition THEN true_value ELSE false_value END`.
- Both branches are evaluated regardless of the condition result. For lazy evaluation, use `CASE...WHEN`.

## Example
```sql
SELECT IIF(score >= 90, 'Pass', 'Fail') AS result FROM #tests;
SELECT IIF(qty > 0, price * qty, 0) AS extended FROM #orders;
SELECT IIF(region IS NULL, 'Unknown', region) AS region FROM #data;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../../../Docs/Reference/Standard_Library.md#7-conditional--null-handling-functions)
- Related: [`COALESCE`](COALESCE.md), [`NULLIF`](NULLIF.md), [`DECODE`](DECODE.md)
