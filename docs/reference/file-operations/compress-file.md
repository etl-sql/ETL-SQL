# COMPRESS / DECOMPRESS FILE

Compresses or extracts individual files and directory hierarchies on disk using `GZIP` or `ZIP` archive formats.

---

## Syntax

### 1. File Compression
```sql
COMPRESS FILE '<source_file_path>' TO '<target_archive_path>' 
[WITH (
  FORMAT = 'GZIP' | 'ZIP',
  OVERWRITE = ON | OFF
)];
```

### 2. Directory Compression
```sql
COMPRESS DIRECTORY '<source_directory_path>' TO '<target_zip_path>' 
[WITH (
  FORMAT = 'ZIP',
  RECURSE = ON | OFF,
  OVERWRITE = ON | OFF
)];
```

### 3. Decompression
```sql
DECOMPRESS FILE '<source_archive_path>' TO '<destination_path>' 
[WITH (
  OVERWRITE = ON | OFF
)];
```

---

## Options & Formats

| Option | Values | Default | Description |
| :--- | :--- | :--- | :--- |
| `FORMAT` | `'GZIP'`, `'ZIP'` | `GZIP` (files), `ZIP` (directories) | Compression algorithm |
| `RECURSE` | `ON`, `OFF` | `ON` | Recursively include subfolders when compressing directories |
| `OVERWRITE` | `ON`, `OFF` | `OFF` | Overwrite destination file if it already exists |

---

## Examples

### 1. Single File Compression & Decompression

```sql
-- Compress CSV export to GZIP archive
COMPRESS FILE 'C:\exports\ledger.csv' 
TO 'C:\exports\ledger.csv.gz' 
WITH (FORMAT = 'GZIP', OVERWRITE = ON);

-- Extract GZIP archive back to staging
DECOMPRESS FILE 'C:\exports\ledger.csv.gz' 
TO 'C:\staging\ledger_unpacked.csv' 
WITH (OVERWRITE = ON);
```

### 2. Compressing Directory Trees

Archive an entire directory of logs into a single `.zip` package:

```sql
COMPRESS DIRECTORY 'C:\logs\2026-08\' 
TO 'C:\archives\august_2026_logs.zip' 
WITH (
  FORMAT = 'ZIP',
  RECURSE = ON,
  OVERWRITE = ON
);

PRINT 'Directory archived successfully.';
```

### 3. Production ETL: Batch Log Ingestion & GZIP Archival

Extract compressed server access logs from a landing folder, decompress, stage into engine memory, filter with regex, and write to analytical warehouse:

```sql
CREATE CONNECTION landing_dir AS DIRECTORY(PATH='C:\telemetry\drops');
CREATE CONNECTION dw          AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Discover all compressed log archives
SELECT file_name, file_path 
INTO #compressed_logs
FROM landing_dir.files
WHERE file_extension = '.gz';

FOREACH @log IN #compressed_logs
BEGIN
  DECLARE @extracted_path VARCHAR = 'C:\staging\' + REPLACE(@log.file_name, '.gz', '');

  -- 2. Decompress archive to local staging
  DECOMPRESS FILE @log.file_path TO @extracted_path WITH (OVERWRITE = ON);

  -- 3. Ingest and parse with CSV connector
  CREATE CONNECTION uncompressed_csv AS FLATFILE(PATH=@extracted_path);
  SELECT log_id, ip_address, status_code, request_path, response_time_ms
  INTO #staged_logs
  FROM uncompressed_csv.data;

  -- 4. Load into analytical warehouse
  INSERT INTO dw.dbo.FactWebLogs (LogId, IpAddress, StatusCode, RequestPath, ResponseTimeMs)
  SELECT log_id, ip_address, status_code, request_path, response_time_ms
  FROM #staged_logs;

  -- 5. Cleanup temporary uncompressed file
  DELETE FILE @extracted_path;
  DROP TABLE #staged_logs;
END;

PRINT 'Batch decompression and log ingestion complete.';
```

---

## References & Related Recipes

- [File Operations Reference](README.md)
- [ENCRYPT FILE](encrypt-file.md)
- [SEND FILE](send-file.md)
- [ETL Cookbook: Automated SFTP Bursting](../../cookbooks/etl/automated-sftp-bursting.md)
- [ETL Cookbook: Batch Directory Ingestion](../../cookbooks/etl/batch-directory-ingester.md)
- [Syntax Index](../../syntax-index.md)
