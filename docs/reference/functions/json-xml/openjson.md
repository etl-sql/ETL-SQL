# OPENJSON
Table-valued function that parses JSON text and returns objects and properties as rows.

**Category:** JSON

## Syntax
`sql
SELECT * FROM OPENJSON(json [, path])
`

## Example
`sql
SELECT * FROM OPENJSON('{"name": "John", "age": 30}');
`

## See Also
- Related: [JSON_VALUE](../json-xml/json_value.md), [JSON_QUERY](../json-xml/json_query.md)

References:
- [Standard Library](../../../guides/getting-started.md)
