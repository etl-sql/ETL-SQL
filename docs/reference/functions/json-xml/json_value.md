# JSON_VALUE

Extracts a scalar value from a JSON string at a specified path.

## Syntax

```sql
JSON_VALUE(json, path)
```

## Parameters

- **json** - JSON string to query.
- **path** - JSONPath expression, such as `'$.name'` or `'$.items[0].id'`.

## Returns

Returns the scalar value at `path` as a string.

## Null Behavior

Returns `NULL` when `json` is `NULL`, the path does not exist, or the path points to an object or array.

## Examples

```sql
DECLARE @json STRING = '{"id": 1, "name": "Alice", "scores": [95, 87]}';
SELECT JSON_VALUE(@json, '$.name') AS name;
```

```sql
SELECT JSON_VALUE(payload, '$.status') AS status
FROM #api_responses;
```

## References

- [Standard Library](../standard-library.md)
- [JSON_QUERY](json_query.md)
- [JSON_MODIFY](json_modify.md)
- [ISJSON](isjson.md)
