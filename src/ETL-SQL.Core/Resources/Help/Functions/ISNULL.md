# ISNULL
Returns a replacement value when the expression is NULL.

**Category:** Logic

## Syntax
```sql
ISNULL(value, replacement)
NVL(value, replacement)
IFNULL(value, replacement)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value` | `ANY` | The expression to test |
| `replacement` | `ANY` | Value to return when `value` is NULL |

## Returns
Same type as inputs — `value` if not NULL, otherwise `replacement`.

## Remarks
- `NVL` (Oracle style) and `IFNULL` (MySQL style) are aliases for `ISNULL`.
- For more than two alternatives, use [`COALESCE`](COALESCE.md).

## Example
```sql
SELECT ISNULL(NULL, 'default');           -- → 'default'
SELECT ISNULL(discount, 0) AS discount FROM #orders;
SELECT NVL(phone, 'N/A') AS phone FROM #contacts;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../../../Docs/Reference/Standard_Library.md#7-conditional--null-handling-functions)
- Related: [`COALESCE`](COALESCE.md), [`NULLIF`](NULLIF.md), [`NVL2`](NVL2.md)
