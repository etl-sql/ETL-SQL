# JSON_ARRAY
Constructs a JSON array string from a list of values.

**Category:** JSON

## Syntax
`sql
JSON_ARRAY(value1, value2, ...)
`

## Returns
STRING â€” The formatted JSON array. Returns NULL if the input is NULL.

## Example
`sql
SELECT JSON_ARRAY(10, 'sales', true); -- â†’ '[10, "sales", true]'
`

## See Also
- Related: [JSON_OBJECT](json_object.md), [JSON_VALUE](../json-xml/json_value.md)

References:
- [Standard Library](../../../guides/getting-started.md)
