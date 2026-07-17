# JSON_EXISTS

Returns whether a JSONPath expression exists in a JSON value.

## Syntax

```sql
JSON_EXISTS(json, path)
```

## Parameters

- **json** - JSON string or expression to inspect.
- **path** - JSONPath expression to test.

## Returns

Returns `1` when the path exists; otherwise returns `0`.

## Null Behavior

Returns `NULL` when `json` or `path` is `NULL`.

## Remarks

- Use `JSON_EXISTS` for filtering before extracting values.
- Use [`JSON_VALUE`](json_value.md) to return scalar values.
- Use [`JSON_QUERY`](json_query.md) to return objects or arrays.

## Examples

```sql
SELECT JSON_EXISTS('{"address": {"city": "Boston"}}', '$.address.city') AS has_city;
```

```sql
SELECT *
FROM #orders
WHERE JSON_EXISTS(payload_json, '$.customer.email') = 1;
```

## References

- [Functions](../README.md)
- [JSON_VALUE](json_value.md)
- [JSON_QUERY](json_query.md)
