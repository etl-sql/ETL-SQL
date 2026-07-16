# JSON_TABLE

Projects rows and columns from nested JSON data.

## Syntax

```sql
SELECT * FROM JSON_TABLE(json, row_path COLUMNS (...))
```

## Parameters

- **json** - JSON string or expression.
- **row_path** - JSONPath expression that selects rows.
- **COLUMNS (...)** - Column projection list with names, types, and paths.

## Returns

Returns a table shaped by the `COLUMNS` clause.

## Null Behavior

Returns no rows when `json` is `NULL` or `row_path` matches no rows.

## Remarks

- Use `JSON_TABLE` when the expected JSON schema is known.
- Use [`OPENJSON`](openjson.md) for exploratory expansion of object properties or array items.

## Examples

```sql
SELECT *
FROM JSON_TABLE(
  '[{"id":1},{"id":2}]',
  '$[*]' COLUMNS (id INT PATH '$.id')
);
```

```sql
SELECT order_id, item_id
FROM #orders
CROSS APPLY JSON_TABLE(
  payload_json,
  '$.items[*]' COLUMNS (item_id INT PATH '$.id')
) AS items;
```

## References

- [Standard Library](../standard-library.md)
- [OPENJSON](openjson.md)
- [JSON_QUERY](json_query.md)
