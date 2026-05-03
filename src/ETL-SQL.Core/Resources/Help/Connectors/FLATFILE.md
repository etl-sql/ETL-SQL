# FLATFILE
Reads and writes delimited text files (CSV, TSV) or fixed-width flat files. The most flexible file-based connector — use it for any text-based tabular data.

Syntax:
  CREATE CONNECTION <name> ON FLATFILE(
    PATH      = 'data.csv',
    DELIMITER = ',',
    HEADER    = ON | OFF,
    ENCODING  = 'UTF-8',
    FORMAT    = 'DELIMITED' | 'FIXED',
    COMPRESS  = ON | OFF,
    ENCRYPT   = ON | OFF,
    PASSWORD  = '<passphrase>'
  );

Options:
  PATH       — file path (required)
  DELIMITER  — column separator character (default comma)
  HEADER     — first row is a header row (default ON)
  ENCODING   — file character encoding (default UTF-8)
  FORMAT     — DELIMITED (default) or FIXED (fixed-width columns)
  QUOTE_CHAR — character used to quote fields (default double-quote)
  NULL_VALUE — string that represents NULL (e.g. 'NULL' or '')
  COMPRESS   — gzip compress on write; auto-detect on read (default OFF)
  ENCRYPT    — AES-encrypt on write; decrypt on read (default OFF)
  PASSWORD   — passphrase for encryption

```sql
CREATE CONNECTION Orders ON FLATFILE(
  PATH      = 'C:\data\orders.csv',
  DELIMITER = ',',
  HEADER    = ON,
  ENCODING  = 'UTF-8'
);

SELECT order_id, customer, amount, order_date
  INTO #orders
  FROM Orders
  WHERE amount > 0;

PRINT 'Orders loaded: ' + @@ROWCOUNT;
```
