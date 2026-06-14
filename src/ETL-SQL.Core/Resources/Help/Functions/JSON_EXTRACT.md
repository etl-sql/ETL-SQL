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
- Related: [JSON_VALUE](JSON_VALUE.md), [JSON_QUERY](JSON_QUERY.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
