# EXTRACTVALUE

Extracts a scalar value from XML using an XPath expression. `EXTRACTVALUE` is an alias for [`XMLVALUE`](xmlvalue.md).

## Syntax

```sql
EXTRACTVALUE(xml, xpath)
```

## Parameters

- **xml** - XML string or XML expression to inspect.
- **xpath** - XPath expression that selects a scalar value.

## Returns

Returns the selected scalar value as `STRING`, or `NULL` when no matching value exists.

## Null Behavior

Returns `NULL` when `xml` or `xpath` is `NULL`.

## Remarks

- Use `EXTRACTVALUE` for MySQL-compatible XML scalar extraction.
- Use [`XMLQUERY`](xmlquery.md) when the result should remain an XML fragment.
- Use [`XMLEXISTS`](xmlexists.md) when filtering by node presence.

## Examples

```sql
SELECT EXTRACTVALUE('<user><name>Alice</name></user>', '/user/name') AS user_name;
```

```sql
SELECT document_id, EXTRACTVALUE(payload_xml, '/order/customer/id') AS customer_id
FROM #documents;
```

## References

- [Standard Library](../standard-library.md)
- [XMLVALUE](xmlvalue.md)
- [XMLQUERY](xmlquery.md)
- [XMLEXISTS](xmlexists.md)
