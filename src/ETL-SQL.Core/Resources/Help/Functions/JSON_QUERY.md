# JSON_QUERY
Extracts an object or array fragment from a JSON string at a specified path.

**Category:** JSON

## Syntax
```sql
JSON_QUERY(json, path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `json` | `STRING` | The JSON string to query |
| `path` | `STRING` | JSONPath expression pointing to an object or array |

## Returns
`STRING` — The JSON fragment (object or array) at the path, or `NULL` if not found.

## Remarks
- Use `JSON_VALUE` for scalar values (strings, numbers, booleans).
- Use `JSON_QUERY` for nested objects and arrays.

## Example
```sql
DECLARE @json STRING = '{"user": {"id": 1, "name": "Alice"}, "tags": ["a", "b"]}';
SELECT JSON_QUERY(@json, '$.user');    -- → '{"id":1,"name":"Alice"}'
SELECT JSON_QUERY(@json, '$.tags');   -- → '["a","b"]'
```

## See Also
- [Standard Library — §11. JSON Functions](../../../../../Docs/Reference/Standard_Library.md#11-json-functions)
- Related: [`JSON_VALUE`](JSON_VALUE.md), [`JSON_MODIFY`](JSON_MODIFY.md)
