# XMLFOREST

Constructs an XML forest (a sequence of XML elements) from the provided arguments.

## Syntax

```sql
XMLFOREST(value1 AS name1, value2 AS name2, ...)
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
SELECT XMLFOREST('John' AS FirstName, 'Doe' AS LastName) AS name_xml;
```

```sql
SELECT XMLFOREST(customer_id AS Id, customer_name AS Name) AS customer_xml
FROM #customers;
```

## References

- [Functions](../README.md)
- [XMLELEMENT](xmlelement.md)
- [XMLATTRIBUTES](xmlattributes.md)
