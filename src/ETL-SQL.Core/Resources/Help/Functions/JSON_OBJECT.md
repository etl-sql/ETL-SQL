# JSON_OBJECT
Constructs a JSON object string from a list of key-value pairs.

**Category:** JSON

## Syntax
`sql
JSON_OBJECT(key1, value1, key2, value2, ...)
`

## Returns
STRING â€” The formatted JSON object.

## Example
`sql
SELECT JSON_OBJECT('name', 'Alice', 'active', true); -- â†’ '{"name":"Alice","active":true}'
`

## See Also
- Related: [JSON_ARRAY](JSON_ARRAY.md), [JSON_VALUE](JSON_VALUE.md)