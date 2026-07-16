# XMLATTRIBUTES

Generates XML attributes from name-value expressions. `XMLATTRIBUTES` is used inside [`XMLELEMENT`](xmlelement.md).

## Syntax

```sql
XMLATTRIBUTES(value1 AS name1, value2 AS name2, ...)
```

## Parameters

- **value** - Attribute value expression.
- **name** - Attribute name.

## Returns

Returns an XML attribute expression for use inside XML construction functions.

## Null Behavior

`NULL` attribute values are omitted or emitted according to ETL-SQL XML construction behavior.

## Remarks

- Use `XMLATTRIBUTES` with `XMLELEMENT` to attach metadata to generated elements.
- Use [`XMLFOREST`](xmlforest.md) for sibling element generation.

## Examples

```sql
SELECT XMLELEMENT('Customer', XMLATTRIBUTES('123' AS id), 'John Doe') AS customer_xml;
```

```sql
SELECT XMLELEMENT('Order', XMLATTRIBUTES(order_id AS id, status AS state), total) AS order_xml
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [XMLELEMENT](xmlelement.md)
- [XMLFOREST](xmlforest.md)
