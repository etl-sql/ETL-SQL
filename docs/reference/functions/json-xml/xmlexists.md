# XMLEXISTS

Returns whether an XPath expression matches at least one node in an XML value.

## Syntax

```sql
XMLEXISTS(xml, xpath)
```

## Parameters

- **xml** - XML string or XML expression to inspect.
- **xpath** - XPath expression to evaluate.

## Returns

Returns `1` when the XPath matches at least one node; otherwise returns `0`.

## Null Behavior

Returns `NULL` when `xml` or `xpath` is `NULL`.

## Remarks

- Use `XMLEXISTS` for filtering XML rows before extracting values.
- Use [`XMLVALUE`](xmlvalue.md) or [`EXTRACTVALUE`](extractvalue.md) to return scalar values.
- Use [`XMLQUERY`](xmlquery.md) to return XML fragments.

## Examples

```sql
SELECT *
FROM #documents
WHERE XMLEXISTS(payload_xml, '/order/customer') = 1;
```

```sql
SELECT document_id
FROM #documents
WHERE XMLEXISTS(payload_xml, '/order/items/item') = 1;
```

## References

- [Standard Library](../standard-library.md)
- [XMLQUERY](xmlquery.md)
- [XMLVALUE](xmlvalue.md)
