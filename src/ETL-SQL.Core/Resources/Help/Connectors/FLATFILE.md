# FLATFILE
Reads and writes delimited text files (CSV, TSV) or fixed-width flat files. The most flexible file-based connector — use it for any text-based tabular data.

Syntax:
```sql
CREATE CONNECTION <name> AS FLATFILE(
  PATH      = 'data.csv',
  DELIMITER = ',',
  HEADER    = ON | OFF,
  ENCODING  = 'UTF-8',
  FORMAT    = 'DELIMITED' | 'FIXED',
  COMPRESS  = ON | OFF,
  ENCRYPT   = ON | OFF,
  PASSWORD  = '<passphrase>'
);
```

Options:
- **PATH** — file path (required)
- **DELIMITER** — column separator character (default comma)
- **HEADER** — first row is a header row (default ON)
- **ENCODING** — file character encoding (default UTF-8)
- **FORMAT** — DELIMITED (default) or FIXED (fixed-width columns)
- **QUOTE_CHAR** — character used to quote fields (default double-quote)
- **NULL_VALUE** — string that represents NULL (e.g. 'NULL' or '')
- **STRICT_SCHEMA** — fail on unaccepted source/template schema drift
- **IGNORE_EXTRA_COLUMNS** — ignore source columns not present in the template schema
- **NULL_MISSING_COLUMNS** — fill missing template columns with NULL
- **MAP_BY_HEADER_NAME** — align by case-insensitive unique source header names
- **COMPRESS** — gzip compress on write; auto-detect on read (default OFF)
- **ENCRYPT** — AES-encrypt on write; decrypt on read (default OFF)
- **PASSWORD** — passphrase for encryption

```sql
CREATE CONNECTION Orders AS FLATFILE(
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

When schema-resilience options change the accepted shape, FLATFILE emits a diagnostic with ignored extra-column count, null-filled missing-column count, and affected row count. Use `EXPECT SCHEMA` after staging when the accepted `#temp` shape is a downstream contract.

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
