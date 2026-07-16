# NULLIF
Returns NULL if two expressions are equal; otherwise returns the first expression.

**Category:** Logic

## Syntax
```sql
NULLIF(value1, value2)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value1` | `ANY` | The value to return if not equal to `value2` |
| `value2` | `ANY` | The comparison value |

## Returns
Same type as `value1` — `NULL` if `value1 = value2`; otherwise `value1`.

## Remarks
- Classic use: avoid division-by-zero: `value / NULLIF(denominator, 0)`.
- Also used with `COALESCE` to treat empty strings as NULL: `COALESCE(NULLIF(TRIM(col), ''), 'default')`.

## Example
```sql
SELECT NULLIF(10, 10);         -- → NULL
SELECT NULLIF(10, 5);          -- → 10
SELECT total / NULLIF(qty, 0) AS unit_price FROM #orders;
SELECT COALESCE(NULLIF(TRIM(region), ''), 'Unknown') FROM #data;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../guides/getting-started.md#7-conditional--null-handling-functions)
- Related: [`COALESCE`](coalesce.md), [`ISNULL`](isnull.md)
