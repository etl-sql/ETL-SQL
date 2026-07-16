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
- Related: [JSON_VALUE](JSON_VALUE.md), [JSON_QUERY](JSON_QUERY.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
