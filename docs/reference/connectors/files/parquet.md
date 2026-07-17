# PARQUET
Reads and writes Apache Parquet columnar files. Parquet is the preferred format for large analytical datasets — it compresses well and supports efficient columnar reads.

Syntax:
  CREATE CONNECTION <name> AS PARQUET(
    PATH        = 'data.parquet',
    COMPRESSION = 'SNAPPY' | 'GZIP' | 'ZSTD' | 'NONE',
    ENCRYPT     = ON | OFF,
    PASSWORD    = '<passphrase>'
  );

Options:
- **PATH** — file path (required)
- **COMPRESSION** — output compression codec (default SNAPPY)
- **ENCRYPT** — AES encrypt/decrypt (default OFF)
- **PASSWORD** — passphrase for encryption

```sql
CREATE CONNECTION EventLog AS PARQUET(
  PATH        = 'C:\data\events_2024.parquet',
  COMPRESSION = 'SNAPPY'
);

SELECT user_id, event_type, event_ts
  INTO #events
  FROM EventLog
  WHERE event_type IN ('login', 'purchase');

-- Write a large result set to Parquet
SELECT * INTO OutParquet FROM #analytics_result;
```

References:
- [Data Connectors](../../../administration/platform/README.md)
