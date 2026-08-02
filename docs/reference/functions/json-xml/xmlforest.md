# XMLFOREST

Constructs an XML forest (a sequence of XML elements) from the provided arguments.

## Syntax

```sql
XMLFOREST(name1, value1, name2, value2, ...)
```

## Parameters

- **valueN** - Value to serialize into an XML element.
- **nameN** - Element name for the corresponding value.

## Returns

Returns the concatenated XML elements as XML text.

## Null Behavior

Returns `NULL` when all inputs are `NULL`.

## Examples

```sql
SELECT XMLFOREST('FirstName', 'John', 'LastName', 'Doe') AS name_xml;
```

```sql
SELECT XMLFOREST('Id', customer_id, 'Name', customer_name) AS customer_xml
FROM #customers;
```

## References

- [Functions](../README.md)
- [XMLELEMENT](xmlelement.md)
- [XMLATTRIBUTES](xmlattributes.md)
