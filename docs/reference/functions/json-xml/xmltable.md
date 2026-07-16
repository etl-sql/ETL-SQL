# XMLTABLE
Table-valued function that projects rows from XML data using XPath expressions.

**Category:** XML

## Syntax
`sql
SELECT * FROM XMLTABLE(xml, row_xpath COLUMNS (...))
`

## Example
`sql
-- Projects structured rows from XML content
SELECT * FROM XMLTABLE('<root><row><id>1</id><name>A</name></row></root>', '/root/row');
`

## See Also
- Related: [XMLVALUE](xmlvalue.md), [XMLQUERY](xmlquery.md)

References:
- [Standard Library](../../../guides/getting-started.md)
