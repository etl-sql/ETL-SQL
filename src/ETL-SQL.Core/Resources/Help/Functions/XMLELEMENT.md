# XMLELEMENT
Constructs an XML element with the specified name, optional attributes, and element content.

**Category:** XML

## Syntax
`sql
XMLELEMENT(name [, attributes] [, content])
`

## Example
`sql
SELECT XMLELEMENT('Emp', XMLATTRIBUTES('true' AS active), 'Jane'); 
-- â†’ '<Emp active="true">Jane</Emp>'
`

## See Also
- Related: [XMLATTRIBUTES](XMLATTRIBUTES.md), [XMLFOREST](XMLFOREST.md)