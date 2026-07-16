# NVL
Returns a replacement when the first argument is NULL. Oracle-style alias for ISNULL.

**Category:** Logic

## Syntax
```sql
NVL(value, replacement)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `value` | `ANY` | The expression to test |
| `replacement` | `ANY` | Value returned when `value` is NULL |

## Returns
Same type as inputs. Identical behavior to [`ISNULL`](../conversion/isnull.md) and [`IFNULL`](../conversion/isnull.md).

## Example
```sql
SELECT NVL(region, 'Unknown') FROM #data;
SELECT NVL(discount, 0) AS discount FROM #orders;
```

## See Also
- [Standard Library — §7. Conditional & Null-Handling Functions](../../../guides/getting-started.md#7-conditional--null-handling-functions)
- Related: [`ISNULL`](../conversion/isnull.md), [`NVL2`](nvl2.md), [`COALESCE`](../conversion/coalesce.md)
