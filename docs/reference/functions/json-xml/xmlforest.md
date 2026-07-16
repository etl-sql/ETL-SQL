# XMLFOREST
Constructs an XML forest (a sequence of XML elements) from the provided arguments.

**Category:** XML

## Syntax
`sql
XMLFOREST(value1 AS name1, value2 AS name2, ...)
`

## Returns
STRING / XML â€” The concatenated XML elements. Returns NULL if all inputs are NULL.

## Example
`sql
SELECT XMLFOREST('John' AS FirstName, 'Doe' AS LastName); -- â†’ '<FirstName>John</FirstName><LastName>Doe</LastName>'
`

## See Also
- Related: [XMLELEMENT](xmlelement.md), [XMLATTRIBUTES](xmlattributes.md)

References:
- [Standard Library](../../../guides/getting-started.md)
