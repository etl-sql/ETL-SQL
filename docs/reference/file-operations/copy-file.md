# COPY
Copies a file or directory from one location to another, including across connections (local, SFTP, S3).

## COPY FILE Syntax
```text
COPY FILE 'source/report.csv' TO 'archive/report.csv';
COPY FILE 'source/report.csv' TO DIRECTORY 'archive';
COPY FILE 'source/report.csv' TO DIRECTORY 'archive' WITH (DATE_SUFFIX = 'yyyyMMdd', SUFFIX_SEPARATOR = '_');
```

## Examples
```sql
-- Copy to an explicit destination file
COPY FILE 'source/report.csv' TO 'archive/report.csv';

-- Cross-connection copy (local to SFTP)
COPY FILE 'output/report.csv' TO SftpConn:'uploads/report.csv';

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
```

## COPY DIRECTORY Syntax
```sql
COPY DIRECTORY 'output/' TO 'archive/2024/';

-- Cross-connection directory copy
COPY DIRECTORY 'reports/' TO S3Conn:'bucket/reports/' WITH (
  OVERWRITE = ON,
  RECURSE   = ON
);
```

## Options
| Option | Values | Default |
|---|---|---|
| OVERWRITE | ON \| OFF | OFF |
| DATE_SUFFIX | .NET date format string, such as `yyyyMMdd` | none |
| SUFFIX_SEPARATOR | String separator before the date suffix | `_` |
| RECURSE | ON \| OFF | ON |

## Notes
- Source and destination can use different connection types (e.g., local to SFTP, SFTP to S3).
- `TO DIRECTORY` derives the destination file name from the source file name. With `DATE_SUFFIX = 'yyyyMMdd'`, `vendor.csv` becomes `vendor_20260722.csv` on July 22, 2026.
- `DATE_SUFFIX` is appended before the file extension and is most useful for archive copies after a send/export step.
- Paths are resolved via `ResolvePath()` — relative paths are anchored to the script's location.
- `COPY DIRECTORY` with `RECURSE = OFF` copies only top-level files.
- See: EXPORT, COMPRESS, CREATE CONNECTION

References:
- [File Operations](README.md)
