# COMPRESS / DECOMPRESS FILE

Compresses a file or directory tree into an archive (`.zip`, `.gz`, `.tar`, or `.tar.gz`/`.tgz`), and extracts archives with path-traversal, link-type, and decompression-ratio guardrails.

---

## Syntax

### 1. File Compression
```sql
COMPRESS FILE '<source_file_path>' [TO '<target_archive_path>']
[WITH (OVERWRITE = ON | OFF, DELETE_SOURCE = ON | OFF, FORMAT = 'ZIP' | 'GZIP' | 'TAR' | 'TAR_GZ')];
```

### 2. Directory Compression
```sql
COMPRESS DIRECTORY '<source_directory_path>' [TO '<target_archive_path>']
[WITH (OVERWRITE = ON | OFF, FORMAT = 'ZIP' | 'TAR' | 'TAR_GZ')];
```

### 3. File and Directory Decompression
```sql
DECOMPRESS FILE '<source_archive_path>' [TO [DIRECTORY] '<destination_path>']
[WITH (OVERWRITE = ON | OFF, DELETE_SOURCE = ON | OFF, FORMAT = 'ZIP' | 'GZIP' | 'TAR' | 'TAR_GZ', PATTERN = '<glob>')];

DECOMPRESS DIRECTORY '<source_archive_path>' [TO '<destination_dir>']
[WITH (OVERWRITE = ON | OFF, FORMAT = 'ZIP' | 'TAR' | 'TAR_GZ', PATTERN = '<glob>')];
```

---

## Options

| Option | Values | Default | Description |
| :--- | :--- | :--- | :--- |
| `OVERWRITE` | `ON`, `OFF` | `ON` | Replace the destination if it already exists; `OFF` fails instead. |
| `DELETE_SOURCE` | `ON`, `OFF` | `OFF` | `COMPRESS FILE` and `DECOMPRESS FILE` only. Delete the source file after the output is written; never on failure or under `SET WHAT_IF ON`. A directory source is refused: remove it with `DELETE DIRECTORY`. |
| `FORMAT` | `'ZIP'`, `'GZIP'`, `'TAR'`, `'TAR_GZ'` | Inferred from extension (`.zip` fallback on `COMPRESS`) | Overrides archive format detection. `'GZIP'` compresses or decompresses a single file only. |
| `PATTERN` | Wildcard glob (e.g. `'*.csv'`, `'sub/*.json'`) | All entries | `DECOMPRESS FILE` and `DECOMPRESS DIRECTORY` only. Extracts only entries matching the glob pattern. |

## Default `TO` Destinations

When `TO` is omitted:

- `COMPRESS FILE 'x.csv'` writes `'x.csv.zip'` (or `'x.csv.gz'` with `FORMAT = 'GZIP'`, `'x.csv.tar'` with `FORMAT = 'TAR'`, `'x.csv.tar.gz'` with `FORMAT = 'TAR_GZ'`).
- `COMPRESS DIRECTORY 'mydir'` writes `'mydir.zip'` (or `'mydir.tar'` / `'mydir.tar.gz'`).
- `DECOMPRESS FILE` / `DECOMPRESS DIRECTORY` strips `.tar.gz`, `.tgz`, `.zip`, `.tar`, or `.gz` in the same directory (for example, `'x.csv.gz'` -> `'x.csv'`, `'x.zip'` -> `'x'`).

- `COMPRESS DIRECTORY` always includes subfolders.
- Inspect archive entries without extracting to disk using [`ARCHIVE_LIST`](../functions/file-path/archive_list.md).
- Any other `WITH` option is a syntax error that lists the accepted options.

---

## Examples

### 1. Single File Compression & Decompression

```sql
-- Compress a CSV export to GZIP using default destination (ledger.csv.gz)
COMPRESS FILE 'C:\exports\ledger.csv'
WITH (FORMAT = 'GZIP', OVERWRITE = ON);

-- Decompress back to ledger.csv using default destination
DECOMPRESS FILE 'C:\exports\ledger.csv.gz'
WITH (OVERWRITE = ON);
```

### 2. Selective Extraction from `.tar.gz` Archives

```sql
-- Inspect archive entries first without extracting
SELECT Name, FullPath, Size, IsDirectory
FROM ARCHIVE_LIST('C:\archives\august_2026_logs.tar.gz');

-- Extract only CSV files into staging
DECOMPRESS FILE 'C:\archives\august_2026_logs.tar.gz'
TO 'C:\staging\logs\'
WITH (PATTERN = '*.csv', OVERWRITE = ON);
```

### 3. Compressing Directory Trees

Archive an entire directory of logs, subfolders included, into a `.tar.gz` package:

```sql
COMPRESS DIRECTORY 'C:\logs\2026-08\'
TO 'C:\archives\august_2026_logs.tar.gz'
WITH (OVERWRITE = ON);

PRINT 'Directory archived successfully.';
```

---

## References & Related Recipes

- [File Operations Reference](README.md)
- [ARCHIVE_LIST](../functions/file-path/archive_list.md)
- [ENCRYPT FILE](encrypt-file.md)
- [SEND FILE](send-file.md)
- [ETL Cookbook: Automated SFTP Bursting](../../cookbooks/etl/automated-sftp-bursting.md)
- [ETL Cookbook: Batch Directory Ingestion](../../cookbooks/etl/batch-directory-ingester.md)
- [Syntax Index](../../syntax-index.md)
