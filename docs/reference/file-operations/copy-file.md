# COPY
Copies a local file or directory from one sandboxed location to another.

## COPY FILE Syntax
```text
COPY FILE 'source/report.csv' TO 'archive/report.csv';
COPY FILE 'source/report.csv' TO DIRECTORY 'archive';
COPY FILE 'source/report.csv' TO DIRECTORY 'archive' WITH (DATE_SUFFIX = 'yyyyMMdd', SUFFIX_SEPARATOR = '_');
COPY FILE '/incoming/report.csv' TO '/archive/report.csv' AT sftp_conn WITH (OVERWRITE = OFF);
```

## Examples
```sql
-- Copy to an explicit destination file
COPY FILE 'source/report.csv' TO 'archive/report.csv';

-- With options
COPY FILE 'source/data.csv' TO 'backup/data.csv' WITH (
  OVERWRITE = ON
);

-- Archive using the source file name plus today's date before the extension
COPY FILE 'source/vendor.csv'
TO DIRECTORY 'backup'
WITH (
  DATE_SUFFIX = 'yyyyMMdd',
  SUFFIX_SEPARATOR = '_'
);

-- Copy a file on a remote connection (SFTP, FTP, S3, AZURE_BLOB, SHAREPOINT)
COPY FILE '/incoming/daily.csv' TO '/archive/daily.csv' AT sftp_conn WITH (OVERWRITE = OFF);
```

## COPY DIRECTORY Syntax
```sql
COPY DIRECTORY 'output/' TO 'archive/2024/';

COPY DIRECTORY 'reports/' TO 'archive/reports/' WITH (OVERWRITE = ON);
```

## Options
| Option | Values | Default |
|---|---|---|
| OVERWRITE | ON \| OFF | ON |
| DATE_SUFFIX | .NET date format string, such as `yyyyMMdd` | none |
| SUFFIX_SEPARATOR | String separator before the date suffix | `_` |

## Notes
- `COPY FILE` works on local sandboxed paths or on a remote connection via `AT <connection>` (`SFTP`, `FTP`, `S3`, `AZURE_BLOB`, `SHAREPOINT`). Use `SEND FILE` or `RECEIVE FILE` for explicit local↔remote transfers.
- On `S3` (`CopyObject`), `AZURE_BLOB` (`StartCopyFromUri`), and `SHAREPOINT` (`CopyTo`), same-connection `COPY FILE ... AT conn` runs server-side. On `SFTP` and `FTP` (and cross-connection copies), the engine downloads to a temporary file under the engine temp root, uploads to the target, and deletes the temporary file on both success and failure.
- `OVERWRITE` defaults to `ON`. With `OVERWRITE = OFF`, an existing target fails before any byte moves.
- Under `SET WHAT_IF ON`, `COPY FILE ... AT conn` logs the plan without touching the remote server.
- `TO DIRECTORY` derives the destination file name from the source file name. With `DATE_SUFFIX = 'yyyyMMdd'`, `vendor.csv` becomes `vendor_20260722.csv` on July 22, 2026.
- `DATE_SUFFIX` is appended before the file extension and is most useful for archive copies after a send/export step.
- Paths are resolved via `ResolvePath()` — relative paths are anchored to the script's location.
- `COPY DIRECTORY` always copies subfolders.
- Any other `WITH` option is a syntax error that lists the accepted options.
- See: EXPORT, COMPRESS, CREATE CONNECTION

References:
- [File Operations](README.md)
