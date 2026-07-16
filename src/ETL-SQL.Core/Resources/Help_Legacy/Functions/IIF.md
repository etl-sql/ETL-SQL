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
- Compiled at parse time to `CASE WHEN condition THEN true_value ELSE false_value END` — IIF *is*
  CASE, exactly as in T-SQL.
- Because it is CASE, evaluation **short-circuits**: the untaken branch is never evaluated, so
  `IIF(x = 0, 0, 1/x)` is safe when `x` is 0.
- Pushes down to any connector as universal `CASE`, not as a T-SQL-only function.
- A `NULL`/UNKNOWN condition selects `false_value` (standard CASE behavior).

## Example
```sql
SELECT IIF(score >= 90, 'Pass', 'Fail') AS result FROM #tests;
SELECT IIF(qty > 0, price * qty, 0) AS extended FROM #orders;
SELECT IIF(region IS NULL, 'Unknown', region) AS region FROM #data;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../../../Docs/Reference/Standard_Library.md#7-conditional--null-handling-functions)
- Related: [`COALESCE`](COALESCE.md), [`NULLIF`](NULLIF.md), [`DECODE`](DECODE.md)
