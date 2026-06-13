# GET_TAGS
Table-valued function that returns a table of metadata tag names defined on a table or column.

**Category:** Lineage & Metadata

## Syntax
`sql
SELECT * FROM GET_TAGS(table_name [, column_name])
`

## Returns
TABLE â€” A table of tag names with a single column alue (VARCHAR).

## Example
`sql
SELECT * FROM GET_TAGS('Customers', 'SSN');
`

## See Also
- Related: [GET_TAG_VALUE](GET_TAG_VALUE.md), [HAS_TAG](HAS_TAG.md)