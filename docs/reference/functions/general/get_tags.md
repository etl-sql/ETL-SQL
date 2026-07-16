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
- Related: [GET_TAG_VALUE](get_tag_value.md), [HAS_TAG](has_tag.md)

References:
- [Standard Library](../../../guides/getting-started.md)
