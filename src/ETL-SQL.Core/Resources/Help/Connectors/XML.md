# XML
Reads and writes XML files. Use ROOT_PATH to specify the XPath expression selecting the repeating element to unpack as rows.

Syntax:
  CREATE CONNECTION <name> AS XML(
    PATH      = 'data.xml',
    ROOT_PATH = '/root/items/item',
    ENCODING  = 'UTF-8',
    COMPRESS  = ON | OFF,
    ENCRYPT   = ON | OFF,
    PASSWORD  = '<passphrase>'
  );

Options:
  PATH       — file path (required)
  ROOT_PATH  — XPath expression to the repeating row element (required for SELECT)
  ENCODING   — character encoding (default UTF-8)
  COMPRESS   — gzip compress/decompress (default OFF)
  ENCRYPT    — AES encrypt/decrypt (default OFF)
  PASSWORD   — passphrase for encryption

```sql
CREATE CONNECTION OrderFeed AS XML(
  PATH      = 'C:\feeds\orders.xml',
  ROOT_PATH = '/Orders/Order'
);

SELECT OrderID, CustomerID, Total, OrderDate
  INTO #orders
  FROM OrderFeed;

PRINT 'Orders loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
