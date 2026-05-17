# JSON_MODIFY
Returns a JSON string with a value at the specified path updated, added, or removed.

**Category:** JSON

## Syntax
```sql
JSON_MODIFY(json, path, new_value)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `json` | `STRING` | The JSON string to modify |
| `path` | `STRING` | JSONPath of the key to set |
| `new_value` | `ANY` | The new value. Pass `NULL` to remove the key |

## Returns
`STRING` — Modified JSON string.

## Example
```sql
DECLARE @json STRING = '{"name": "Alice", "status": "active"}';
SELECT JSON_MODIFY(@json, '$.status', 'inactive');     -- → '{"name":"Alice","status":"inactive"}'
SELECT JSON_MODIFY(@json, '$.score', 99);              -- → adds $.score
SELECT JSON_MODIFY(@json, '$.status', NULL);           -- → removes $.status
```

## See Also
- [Standard Library — §11. JSON Functions](../../../../../Docs/Reference/Standard_Library.md#11-json-functions)
- Related: [`JSON_VALUE`](JSON_VALUE.md), [`JSON_QUERY`](JSON_QUERY.md)
