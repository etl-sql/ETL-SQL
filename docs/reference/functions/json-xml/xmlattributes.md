# XMLATTRIBUTES

Generates XML attributes from name-value expressions. `XMLATTRIBUTES` is used inside [`XMLELEMENT`](xmlelement.md).

## Syntax

```sql
XMLATTRIBUTES(name1, value1, name2, value2, ...)
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
SELECT XMLATTRIBUTES('id', '123', 'status', 'active') AS customer_attributes;
```

```sql
SELECT XMLATTRIBUTES('id', order_id, 'state', status) AS order_attributes
FROM #orders;
```

## References

- [Functions](../README.md)
- [XMLELEMENT](xmlelement.md)
- [XMLFOREST](xmlforest.md)
