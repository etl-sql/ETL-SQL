# JSON_MODIFY

Returns a JSON string with a value at the specified path updated, added, or removed.

## Syntax

```sql
JSON_MODIFY(json, path, new_value)
```

## Parameters

- **json** - JSON string to modify.
- **path** - JSONPath of the key to set.
- **new_value** - Value to write. Pass `NULL` to remove the key.

## Returns

Returns the modified JSON string.

## Null Behavior

Returns `NULL` when `json` is `NULL`.

## Examples

```sql
DECLARE @json STRING = '{"name": "Alice", "status": "active"}';
SELECT JSON_MODIFY(@json, '$.status', 'inactive') AS updated_json;
```

```sql
UPDATE #profiles
SET profile_json = JSON_MODIFY(profile_json, '$.lastSeen', GETDATE());
```

## References

- [Functions](../README.md)
- [JSON_VALUE](json_value.md)
- [JSON_QUERY](json_query.md)
