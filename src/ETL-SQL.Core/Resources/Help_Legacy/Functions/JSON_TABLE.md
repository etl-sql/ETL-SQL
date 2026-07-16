# JSON_TABLE
Table-valued function that projects a tabular schema from nested JSON rows.

**Category:** JSON

## Syntax
`sql
SELECT * FROM JSON_TABLE(json, row_path COLUMNS (...))
`

## Example
`sql
-- Project rows from JSON array
SELECT * FROM JSON_TABLE('[{"id":1},{"id":2}]', '$' COLUMNS (id INT PATH '$.id'));
`

## See Also
- Related: [OPENJSON](OPENJSON.md), [JSON_QUERY](JSON_QUERY.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
