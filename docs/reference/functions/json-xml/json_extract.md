# JSON_EXTRACT

Extracts a scalar value from JSON using a JSONPath expression. `JSON_EXTRACT` is an alias for [`JSON_VALUE`](json_value.md).

## Syntax

```sql
JSON_EXTRACT(json, path)
```

## Parameters

- **json** - JSON string or expression to inspect.
- **path** - JSONPath expression that selects a scalar value.

## Returns

Returns the selected scalar value, or `NULL` when the path does not exist.

## Null Behavior

Returns `NULL` when `json` or `path` is `NULL`.

## Remarks

- Use `JSON_EXTRACT` when porting scripts from systems that use MySQL-style JSON extraction naming.
- Use [`JSON_QUERY`](json_query.md) for object or array fragments.
- Use [`OPENJSON`](openjson.md) to expand JSON into rows.

## Examples

```sql
SELECT JSON_EXTRACT('{"name": "Alice"}', '$.name') AS name;
```

```sql
SELECT order_id, JSON_EXTRACT(payload_json, '$.customer.email') AS email
FROM #orders;
```

## References

- [Functions](../README.md)
- [JSON_VALUE](json_value.md)
- [JSON_QUERY](json_query.md)
- [OPENJSON](openjson.md)
