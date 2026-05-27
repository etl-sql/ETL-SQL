# JSON
Reads and writes JSON files. Use ROOT_PATH to navigate to the array node to unpack as rows.

Syntax:
  CREATE CONNECTION <name> AS JSON(
    PATH      = 'data.json',
    ROOT_PATH = '$.items',
    ENCODING  = 'UTF-8',
    COMPRESS  = ON | OFF,
    ENCRYPT   = ON | OFF,
    PASSWORD  = '<passphrase>'
  );

Options:
  PATH       — file path (required)
  ROOT_PATH  — JSONPath expression pointing to the array node (default '$' = root)
  ENCODING   — file character encoding (default UTF-8)
  COMPRESS   — gzip compress/decompress (default OFF)
  ENCRYPT    — AES encrypt/decrypt (default OFF)
  PASSWORD   — passphrase for encryption

```sql
CREATE CONNECTION ApiDump AS JSON(
  PATH      = 'C:\data\api_response.json',
  ROOT_PATH = '$.data.orders'
);

SELECT id, customer_id, total, created_at
  INTO #orders
  FROM ApiDump;

PRINT 'Orders loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
