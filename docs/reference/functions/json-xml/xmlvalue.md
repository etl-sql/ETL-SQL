# XMLVALUE
Extracts a scalar value from an XML string using an XPath expression. Alias: EXTRACTVALUE.

**Category:** XML

## Syntax
```sql
XMLVALUE(xml, xpath)
EXTRACTVALUE(xml, xpath)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `xml` | `STRING` | The XML string to query |
| `xpath` | `STRING` | XPath 1.0 expression |

## Returns
`STRING` — The text value of the first matching node, or `NULL` if not found.

## Example
```sql
DECLARE @xml STRING = '<order><id>42</id><status>shipped</status></order>';
SELECT XMLVALUE(@xml, '/order/id');      -- → '42'
SELECT XMLVALUE(@xml, '/order/status'); -- → 'shipped'
SELECT XMLVALUE(response_xml, '//Price/text()') AS price FROM #api;
```

## See Also
- [Standard Library — §12. XML Functions](../../../guides/getting-started.md#12-xml-functions)
- Related: [`XMLEXISTS`](xmlexists.md), [`XMLQUERY`](xmlquery.md), [`XMLTABLE`](xmltable.md)
