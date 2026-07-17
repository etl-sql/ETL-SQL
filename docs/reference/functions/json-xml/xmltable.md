# XMLTABLE

Projects rows and columns from XML data using XPath expressions.

## Syntax

```sql
SELECT * FROM XMLTABLE(xml, row_xpath COLUMNS (...))
```

## Parameters

- **xml** - XML string or XML expression.
- **row_xpath** - XPath expression that selects rows.
- **COLUMNS (...)** - Column projection list with names, types, and XPath expressions.

## Returns

Returns a table shaped by the `COLUMNS` clause.

## Null Behavior

Returns no rows when `xml` is `NULL` or `row_xpath` matches no rows.

## Remarks

- Use `XMLTABLE` when XML needs to be transformed into relational rows.
- Use [`XMLVALUE`](xmlvalue.md) for scalar extraction.
- Use [`XMLQUERY`](xmlquery.md) for XML fragments.

## Examples

```sql
SELECT *
FROM XMLTABLE(
  '<root><row><id>1</id><name>A</name></row></root>',
  '/root/row' COLUMNS (
    id INT PATH 'id',
    name VARCHAR(100) PATH 'name'
  )
);
```

## References

- [Functions](../README.md)
- [XMLVALUE](xmlvalue.md)
- [XMLQUERY](xmlquery.md)
