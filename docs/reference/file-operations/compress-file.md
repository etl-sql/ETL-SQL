# COMPRESS / DECOMPRESS FILE

Writes a file or a directory tree into a ZIP archive, and extracts ZIP archives. ZIP is the only
format: `COMPRESS` writes ZIP whatever the destination extension, and `DECOMPRESS` reads ZIP only.

---

## Syntax

### 1. File Compression
```sql
COMPRESS FILE '<source_file_path>' TO '<target_zip_path>' [WITH (OVERWRITE = ON | OFF, DELETE_SOURCE = ON | OFF)];
```

### 2. Directory Compression
```sql
COMPRESS DIRECTORY '<source_directory_path>' TO '<target_zip_path>' [WITH (OVERWRITE = ON | OFF)];
```

### 3. Decompression
```sql
DECOMPRESS FILE '<source_zip_path>' TO '<destination_path>' [WITH (OVERWRITE = ON | OFF, DELETE_SOURCE = ON | OFF)];
```

---

## Options

| Option | Values | Default | Description |
| :--- | :--- | :--- | :--- |
| `OVERWRITE` | `ON`, `OFF` | `ON` | Replace the destination if it already exists; `OFF` fails instead |
| `DELETE_SOURCE` | `ON`, `OFF` | `OFF` | `COMPRESS FILE` and `DECOMPRESS FILE` only. Delete the source file after the output is written; never on failure or under `SET WHAT_IF ON`. A directory source is refused: remove it with `DELETE DIRECTORY`. |

- `COMPRESS DIRECTORY` always includes subfolders.
- There is no `FORMAT` option. GZIP (`.gz`) archives are not supported.
- Any other `WITH` option is a syntax error that lists the accepted options.

---

## Examples

### 1. Single File Compression & Decompression

```sql
-- Compress a CSV export into a ZIP archive
COMPRESS FILE 'C:\exports\ledger.csv'
TO 'C:\exports\ledger.zip'
WITH (OVERWRITE = ON);

-- Extract the archive into a staging folder
DECOMPRESS FILE 'C:\exports\ledger.zip'
TO 'C:\staging\ledger\'
WITH (OVERWRITE = ON);
```

### 2. Compressing Directory Trees

Archive an entire directory of logs, subfolders included, into a single `.zip` package:

```sql
COMPRESS DIRECTORY 'C:\logs\2026-08\'
TO 'C:\archives\august_2026_logs.zip'
WITH (OVERWRITE = ON);

PRINT 'Directory archived successfully.';
```

### 3. Production ETL: Batch Log Ingestion from ZIP Archives

Extract zipped server access logs from a landing folder, stage each one in engine memory, and load it into the warehouse. Each archive holds one `access.csv`:

```sql
CREATE CONNECTION landing_dir AS DIRECTORY(PATH='C:\telemetry\drops');
CREATE CONNECTION dw          AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Discover all compressed log archives
SELECT file_name, file_path 
INTO #compressed_logs
FROM landing_dir.files
WHERE file_extension = '.zip';

FOREACH @log IN #compressed_logs
BEGIN
  DECLARE @extracted_dir VARCHAR = 'C:\staging\' + REPLACE(@log.file_name, '.zip', '') + '\';

  -- 2. Extract the archive into its own staging folder
  DECOMPRESS FILE @log.file_path TO @extracted_dir WITH (OVERWRITE = ON);

  -- 3. Ingest and parse with CSV connector
  CREATE CONNECTION uncompressed_csv AS FLATFILE(PATH=@extracted_dir + 'access.csv');
  SELECT log_id, ip_address, status_code, request_path, response_time_ms
  INTO #staged_logs
  FROM uncompressed_csv.data;

  -- 4. Load into analytical warehouse
  INSERT INTO dw.dbo.FactWebLogs (LogId, IpAddress, StatusCode, RequestPath, ResponseTimeMs)
  SELECT log_id, ip_address, status_code, request_path, response_time_ms
  FROM #staged_logs;

  -- 5. Clean up the extracted folder
  DELETE DIRECTORY @extracted_dir;
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
