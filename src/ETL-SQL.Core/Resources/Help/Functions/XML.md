XML Functions
=============

Extract and query data from XML strings using XPath expressions.

Scalar Extraction
-----------------
  XMLVALUE(xml, xpath)          Return the text content of the first matching node.
  EXTRACTVALUE(xml, xpath)      Alias for XMLVALUE.
  XMLEXISTS(xml, xpath)         Return 1 if the XPath matches any node, 0 otherwise.
  XMLQUERY(xml, xpath)          Return the matching XML fragment as a string.

```sql
DECLARE @xml VARCHAR = '<root><name>Alice</name><age>30</age></root>';

SELECT XMLVALUE(@xml, '/root/name')      -- 'Alice'
SELECT XMLVALUE(@xml, '/root/age')       -- '30'
SELECT XMLEXISTS(@xml, '/root/name')     -- 1
SELECT XMLEXISTS(@xml, '/root/missing')  -- 0
SELECT XMLQUERY(@xml, '/root')           -- '<root><name>Alice</name><age>30</age></root>'
```

Table Expansion
---------------
  XMLTABLE(xml, xpath)
      Expand the nodes matched by xpath into table rows.
      Each node becomes one row; child elements become columns.

```sql
DECLARE @data VARCHAR = '
<employees>
  <employee><id>1</id><name>Alice</name></employee>
  <employee><id>2</id><name>Bob</name></employee>
</employees>';

SELECT id, name
FROM XMLTABLE(@data, '/employees/employee');
-- Rows: (1, Alice), (2, Bob)
```

Building XML
------------
  XMLELEMENT(name, content)           Construct <name>content</name>.
  XMLATTRIBUTES(n1, v1, n2, v2, ...)  Build an attribute string for use inside an element.
  XMLFOREST(n1, v1, n2, v2, ...)      Build a sequence of elements from name/value pairs.

```sql
SELECT XMLELEMENT('product', 'Widget')
-- '<product>Widget</product>'

SELECT XMLELEMENT('item', XMLATTRIBUTES('id', 42, 'type', 'A'))
-- '<item id="42" type="A"/>'

SELECT XMLFOREST('name', 'Alice', 'dept', 'Sales')
-- '<name>Alice</name><dept>Sales</dept>'
```

Notes
-----
  - XPath uses standard notation: /root/child, /root/child[1], /root/@attr.
  - XMLVALUE returns NULL when the path does not match or the match is an element.
  - XMLQUERY returns a string; parse it with a second XMLVALUE call if needed.
  - For JSON data see HELP FUNCTIONS JSON.
