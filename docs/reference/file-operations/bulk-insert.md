BULK INSERT loads a flat file directly into a connection table in high-throughput batches, bypassing the #temp table staging step.

Syntax:
  BULK INSERT <target_table> FROM <file_path>
    AT <connection>
    WITH (
      BATCH_SIZE     = n,
      MAX_ERRORS     = n,
      ERROR_LOG_PATH = 'path',
      FIRST_ROW      = n,
      LAST_ROW       = n
    );

Options:
- **BATCH_SIZE** — rows per commit batch (default 1000)
- **MAX_ERRORS** — tolerated parse errors before aborting (default 0)
- **ERROR_LOG_PATH** — path to write rejected rows; omit to abort on first error
- **FIRST_ROW** — skip header or preamble rows (1-based)
- **LAST_ROW** — stop loading after this row number

```sql
BULK INSERT dbo.StagedOrders FROM 'C:\data\orders_2024.csv'
  AT SalesDB
  WITH (
    BATCH_SIZE     = 5000,
    MAX_ERRORS     = 10,
    ERROR_LOG_PATH = 'C:\logs\bulk_errors.txt',
    FIRST_ROW      = 2
  );

PRINT 'Loaded: ' + @@ROWCOUNT;
```

For files with column headers, set FIRST_ROW = 2.
Use FLATFILE connections (via CREATE CONNECTION) for full parsing control including delimiter, encoding, and fixed-width formats.

References:
- [File Operations](README.md)
