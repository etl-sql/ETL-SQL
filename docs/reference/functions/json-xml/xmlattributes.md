# XMLATTRIBUTES
Generates XML attributes from the provided name-value expressions. Used inside XMLELEMENT.

**Category:** XML

## Syntax
`sql
XMLATTRIBUTES(value1 AS name1, value2 AS name2, ...)
`

## Example
`sql
SELECT XMLELEMENT('Customer', XMLATTRIBUTES('123' AS id), 'John Doe');
-- â†’ '<Customer id="123">John Doe</Customer>'
`

## See Also
- Related: [XMLELEMENT](xmlelement.md), [XMLFOREST](xmlforest.md)

References:
- [Standard Library](../../../guides/getting-started.md)
