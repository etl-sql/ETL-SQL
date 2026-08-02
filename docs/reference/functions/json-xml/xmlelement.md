# XMLELEMENT

Constructs an XML element with the specified name, optional attributes, and optional content.

## Syntax

```sql
XMLELEMENT(name [, attributes] [, content])
```

## Parameters

- **name** - Element name.
- **attributes** - Optional [`XMLATTRIBUTES`](xmlattributes.md) expression.
- **content** - Optional element content.

## Returns

Returns an XML string.

## Null Behavior

Returns `NULL` when `name` is `NULL`.

## Remarks

- Use `XMLELEMENT` to build small XML payloads from relational values.
- Use [`XMLFOREST`](xmlforest.md) when constructing multiple sibling elements.

## Examples

```sql
SELECT XMLELEMENT('Emp', 'Jane') AS employee_xml;
```

```sql
SELECT XMLELEMENT('Customer', customer_name) AS customer_xml
FROM #customers;
```

## References

- [Functions](../README.md)
- [XMLATTRIBUTES](xmlattributes.md)
- [XMLFOREST](xmlforest.md)
