# STRING_AGG
Concatenates string values within a group, separated by a delimiter.

**Category:** Aggregate

## Syntax
```sql
STRING_AGG(expression, separator)
STRING_AGG(expression, separator) WITHIN GROUP (ORDER BY col [ASC|DESC])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `STRING` | The column or expression to aggregate |
| `separator` | `STRING` | Delimiter placed between values |

## Returns
`STRING` — All non-NULL values joined with the separator. Returns `NULL` if all values are NULL.

## Example
```sql
SELECT STRING_AGG(name, ', ') AS all_names FROM #team;
SELECT order_id, STRING_AGG(sku, ',') WITHIN GROUP (ORDER BY sku) AS items
  FROM #order_lines GROUP BY order_id;
```

## See Also
- [Standard Library — §3.3 Concatenation & Splitting](../../../guides/getting-started.md#33-concatenation--splitting)
- Related: [`CONCAT_WS`](../string/concat_ws.md), [`LISTAGG`](../general/listagg.md)
