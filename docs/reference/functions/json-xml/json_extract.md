# JSON_EXTRACT
Extracts a scalar value from a JSON string using a JSONPath expression. Alias for JSON_VALUE.

**Category:** JSON

## Syntax
`sql
JSON_EXTRACT(json, path)
`

## Example
`sql
SELECT JSON_EXTRACT('{"name": "Alice"}', '$.name'); -- â†’ 'Alice'
`

## See Also
- Related: [JSON_VALUE](../json-xml/json_value.md), [JSON_QUERY](../json-xml/json_query.md)

References:
- [Standard Library](../../../guides/getting-started.md)
