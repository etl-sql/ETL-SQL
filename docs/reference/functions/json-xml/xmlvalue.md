# XMLVALUE

Extracts a scalar value from an XML string using an XPath expression. Alias: EXTRACTVALUE.

## Syntax

```sql
XMLVALUE(xml, xpath)
EXTRACTVALUE(xml, xpath)
```

## Parameters

- **xml** - XML string to query.
- **xpath** - XPath 1.0 expression.

## Returns

Returns the text value of the first matching node.

## Null Behavior

Returns `NULL` when `xml` is `NULL` or no matching node is found.

## Examples

```sql
DECLARE @xml STRING = '<order><id>42</id><status>shipped</status></order>';
SELECT XMLVALUE(@xml, '/order/id') AS order_id;
```

```sql
SELECT XMLVALUE(response_xml, '//Price/text()') AS price
FROM #api;
```

## References

- [Standard Library](../standard-library.md)
- [EXTRACTVALUE](extractvalue.md)
- [XMLEXISTS](xmlexists.md)
- [XMLQUERY](xmlquery.md)
- [XMLTABLE](xmltable.md)
