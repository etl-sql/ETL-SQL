# COALESCE
Returns the first non-NULL value from a list of expressions.

**Category:** Logic

## Syntax
```sql
COALESCE(value1, value2, ...)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value1` | `ANY` | First value to test |
| `value2` | `ANY` | Second value (used if `value1` is NULL) |
| `...` | `ANY` | Additional fallback values (variadic) |

## Returns
Same type as inputs — the first non-NULL value in the list, or `NULL` if all are NULL.

## Remarks
- Short-circuit: evaluation stops at the first non-NULL argument.
- Equivalent to `CASE WHEN v1 IS NOT NULL THEN v1 WHEN v2 IS NOT NULL THEN v2 ... END`.

## Example
```sql
SELECT COALESCE(NULL, NULL, 'fallback');    -- → 'fallback'
SELECT COALESCE(nickname, first_name, 'Unknown') AS display_name FROM #users;
SELECT COALESCE(NULLIF(TRIM(region), ''), 'Unknown') AS region FROM #staging;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../../../Docs/Reference/Standard_Library.md#7-conditional--null-handling-functions)
- Related: [`ISNULL`](ISNULL.md), [`NULLIF`](NULLIF.md), [`IIF`](IIF.md)
