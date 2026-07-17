# XMLQUERY

Returns an XML fragment selected by an XPath expression.

## Syntax

```sql
XMLQUERY(xml, xpath)
```

## Parameters

- **xml** - XML string or XML expression to inspect.
- **xpath** - XPath expression that selects the fragment.

## Returns

Returns the selected XML fragment as `STRING`, or `NULL` when the expression does not match.

## Null Behavior

Returns `NULL` when `xml` or `xpath` is `NULL`.

## Remarks

- Use `XMLQUERY` when the result should remain XML.
- Use [`XMLVALUE`](xmlvalue.md) or [`EXTRACTVALUE`](extractvalue.md) for scalar values.
- Use [`XMLEXISTS`](xmlexists.md) when filtering by node presence.

## Examples

```sql
SELECT XMLQUERY(payload_xml, '/order/customer') AS customer_fragment
FROM #documents;
```

```sql
SELECT document_id, XMLQUERY(payload_xml, '/order/items') AS item_xml
FROM #documents
WHERE XMLEXISTS(payload_xml, '/order/items/item') = 1;
```

## References

- [Functions](../README.md)
- [XMLEXISTS](xmlexists.md)
- [XMLVALUE](xmlvalue.md)
