# MOVE FILE
Moves a file from one location to another. Use `TO DIRECTORY` when the destination should keep the source file name and optionally add a date suffix.

## Syntax
```text
MOVE FILE 'source_path' TO 'destination_path' [WITH (OVERWRITE = ON|OFF, DATE_SUFFIX = 'format', SUFFIX_SEPARATOR = '_')];
MOVE FILE 'source_path' TO DIRECTORY 'destination_dir' [WITH (OVERWRITE = ON|OFF, DATE_SUFFIX = 'format', SUFFIX_SEPARATOR = '_')];
```

## Examples
```sql
-- Move to an explicit destination file
MOVE FILE 'C:\tmp\outbound\vendor.csv'
TO 'C:\tmp\archive\vendor.csv'
WITH (OVERWRITE = ON);

-- Archive with today's date before the extension
MOVE FILE 'C:\tmp\outbound\vendor.csv'
TO DIRECTORY 'C:\tmp\sent'
WITH (DATE_SUFFIX = 'yyyyMMdd');

-- Use a custom separator
MOVE FILE 'C:\tmp\outbound\vendor.csv'
TO DIRECTORY 'C:\tmp\sent'
WITH (
  DATE_SUFFIX = 'yyyyMMdd',
  SUFFIX_SEPARATOR = '-'
);
```

## Options
| Option | Values | Default |
|---|---|---|
| `OVERWRITE` | `ON` \| `OFF` | `ON` |
| `DATE_SUFFIX` | .NET date format string, such as `yyyyMMdd` | none |
| `SUFFIX_SEPARATOR` | String separator before the date suffix | `_` |

## Notes
- `TO DIRECTORY` derives the destination file name from the source file name.
- `DATE_SUFFIX` is appended before the file extension. On July 22, 2026, `vendor.csv` becomes `vendor_20260722.csv`.
- Paths are resolved through the engine path boundary before the file is moved.

References:
- [File Operations](README.md)
