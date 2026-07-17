# SET ALLOW_FILE_TYPE_ACCESS
Controls whether file extensions outside the global whitelist are permitted, or adds a specific extension to the session whitelist.

## Syntax
```sql
SET ALLOW_FILE_TYPE_ACCESS = ON|OFF;
SET ALLOW_FILE_TYPE_ACCESS = '<.ext>';
```

## Parameters
- **ON** — Allow all file extensions, not just those on the global whitelist.
- **OFF** — Restrict to the global whitelist only (default).
- **'.ext'** — Add a specific extension (e.g., `'.parquet'`) to the session whitelist without opening all extensions.

## Example
```sql
-- Allow a specific non-standard extension
SET ALLOW_FILE_TYPE_ACCESS = '.dat';

CREATE CONNECTION src AS FLATFILE(PATH='C:\data\export.dat');
SELECT * FROM src.[export.dat] INTO #data;

-- Or open all extensions (less secure)
SET ALLOW_FILE_TYPE_ACCESS = ON;
```

## Notes
- Produces an audit entry. The path must be within a Safe Zone.
- Default: OFF.

## References
- [SET Commands](README.md)
