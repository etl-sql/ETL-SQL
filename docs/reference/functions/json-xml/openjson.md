# OPENJSON

Parses JSON text and returns object properties or array elements as rows.

## Syntax

```sql
SELECT * FROM OPENJSON(json [, path])
```

## Parameters

- **json** - JSON string or expression to parse.
- **path** - Optional JSONPath expression that selects the object or array to expand.

## Returns

Returns a table of JSON entries. The row shape follows ETL-SQL's JSON table-valued function behavior for key, value, and type metadata.

## Null Behavior

Returns no rows when `json` is `NULL` or the selected path does not exist.

## Remarks

- Use `OPENJSON` when a JSON object or array needs to become relational rows.
- Use [`JSON_VALUE`](json_value.md) for scalar extraction.
- Use [`JSON_QUERY`](json_query.md) for object or array fragments.

## Examples

```sql
SELECT *
FROM OPENJSON('{"name": "John", "age": 30}');
```

```sql
SELECT item.value
FROM #orders
CROSS APPLY OPENJSON(payload_json, '$.items') AS item;
```

## References

- [Functions](../README.md)
- [JSON_VALUE](json_value.md)
- [JSON_QUERY](json_query.md)
- [JSON_TABLE](json_table.md)
