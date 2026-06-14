# GET_TAG_VALUE
Retrieves the metadata tag value assigned to a specific table or column.

**Category:** Lineage & Metadata

## Syntax
`sql
GET_TAG_VALUE(table_name, column_name, tag_name)
`

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| 	able_name | VARCHAR / STRING | Name of the table |
| column_name | VARCHAR / STRING | Name of the column |
| 	ag_name | VARCHAR / STRING | Name of the metadata tag to retrieve |

## Returns
STRING â€” The tag value, or NULL if the tag does not exist.

## Example
`sql
SELECT GET_TAG_VALUE('Customers', 'SSN', 'PII_LEVEL'); -- â†’ 'High'
`

## See Also
- Related: [GET_TAGS](GET_TAGS.md), [HAS_TAG](HAS_TAG.md)

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
