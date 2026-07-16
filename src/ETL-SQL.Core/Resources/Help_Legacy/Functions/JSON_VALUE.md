# JSON_VALUE
Extracts a scalar value from a JSON string at a specified path.

**Category:** JSON

## Syntax
```sql
JSON_VALUE(json, path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `json` | `STRING` | The JSON string to query |
| `path` | `STRING` | JSONPath expression (e.g., `'$.name'`, `'$.items[0].id'`) |

## Returns
`STRING` — The scalar value at the path, or `NULL` if the path doesn't exist or points to an object/array.

## Example
```sql
DECLARE @json STRING = '{"id": 1, "name": "Alice", "scores": [95, 87]}';
SELECT JSON_VALUE(@json, '$.name');        -- → 'Alice'
SELECT JSON_VALUE(@json, '$.scores[0]');  -- → '95'
SELECT JSON_VALUE(payload, '$.status') AS status FROM #api_responses;
```

## See Also
- [Standard Library — §11. JSON Functions](../../../../../Docs/Reference/Standard_Library.md#11-json-functions)
- Related: [`JSON_QUERY`](JSON_QUERY.md), [`JSON_MODIFY`](JSON_MODIFY.md), [`ISJSON`](ISJSON.md)
