# EXTRACTVALUE
Extracts a scalar value from an XML string using an XPath expression. Alias for XMLVALUE.

**Category:** XML

## Syntax
`sql
EXTRACTVALUE(xml, xpath)
`

## Example
`sql
SELECT EXTRACTVALUE('<user><name>Alice</name></user>', '/user/name'); -- â†’ 'Alice'
`

## See Also
- Related: [XMLVALUE](xmlvalue.md), [XMLQUERY](xmlquery.md)

References:
- [Standard Library](../../../guides/getting-started.md)
