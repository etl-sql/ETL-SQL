# COPY
Copies a file or directory from one location to another, including across connections (local, SFTP, S3).

## COPY FILE Syntax
```sql
COPY FILE 'source/report.csv' TO 'archive/report.csv';

-- Cross-connection copy (local to SFTP)
COPY FILE 'output/report.csv' TO SftpConn:'uploads/report.csv';

-- With options
COPY FILE 'source/data.csv' TO 'backup/data.csv' WITH (
  OVERWRITE = ON
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
| RECURSE | ON \| OFF | ON |

## Notes
- Source and destination can use different connection types (e.g., local to SFTP, SFTP to S3).
- Paths are resolved via `ResolvePath()` — relative paths are anchored to the script's location.
- `COPY DIRECTORY` with `RECURSE = OFF` copies only top-level files.
- See: EXPORT, COMPRESS, CREATE CONNECTION

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
