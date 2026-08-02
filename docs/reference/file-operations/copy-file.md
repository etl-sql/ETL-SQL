# COPY
Copies a local file or directory from one sandboxed location to another.

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

COPY DIRECTORY 'reports/' TO 'archive/reports/' WITH (
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
- `COPY` is a local sandboxed file operation. Use `SEND FILE` or `RECEIVE FILE` for connector transfers.
- `TO DIRECTORY` derives the destination file name from the source file name. With `DATE_SUFFIX = 'yyyyMMdd'`, `vendor.csv` becomes `vendor_20260722.csv` on July 22, 2026.
- `DATE_SUFFIX` is appended before the file extension and is most useful for archive copies after a send/export step.
- Paths are resolved via `ResolvePath()` — relative paths are anchored to the script's location.
- `COPY DIRECTORY` with `RECURSE = OFF` copies only top-level files.
- See: EXPORT, COMPRESS, CREATE CONNECTION

References:
- [File Operations](README.md)
