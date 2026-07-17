# JSON_QUERY

Extracts an object or array fragment from a JSON string at a specified path.

## Syntax

```sql
JSON_QUERY(json, path)
```

## Parameters

- **json** - JSON string to query.
- **path** - JSONPath expression pointing to an object or array.

## Returns

Returns the JSON object or array fragment at `path`.

## Null Behavior

Returns `NULL` when `json` is `NULL`, `path` is missing, or the path does not point to an object or array.

## Remarks

- Use `JSON_VALUE` for scalar values (strings, numbers, booleans).
- Use `JSON_QUERY` for nested objects and arrays.

## Examples

```sql
DECLARE @json STRING = '{"user": {"id": 1, "name": "Alice"}, "tags": ["a", "b"]}';
SELECT JSON_QUERY(@json, '$.user') AS user_json;
```

```sql
SELECT JSON_QUERY(payload, '$.items') AS items_json
FROM #api_responses;
```

## References

- [Functions](../README.md)
- [JSON_VALUE](json_value.md)
- [JSON_MODIFY](json_modify.md)
