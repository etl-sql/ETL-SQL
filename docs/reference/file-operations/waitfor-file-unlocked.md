# WAITFOR FILE UNLOCKED
Blocks pipeline execution until a file arrives on the filesystem and is fully unlocked (not being
written to by another process).

## Syntax
```sql
WAITFOR FILE UNLOCKED '<path>' [WITH(TIMEOUT = <seconds>, POLL_INTERVAL_MS = <ms>)];
```

## Example
```sql
-- Wait up to two minutes for an upstream export to finish writing
WAITFOR FILE UNLOCKED 'landing/daily_export.csv' WITH (
  TIMEOUT          = 120,
  POLL_INTERVAL_MS = 1000
);

SELECT * INTO #daily FROM CSV('landing/daily_export.csv');
```

## Options
| Option | Description | Default |
| :--- | :--- | :--- |
| `TIMEOUT` | Maximum seconds to wait before throwing a timeout exception. | `30` |
| `POLL_INTERVAL_MS` | Polling check interval in milliseconds. | `500` |

## References
- [Advanced File Operations](advanced-file-operations.md)
- [FILE Operations](file.md)
- [File Operations index](README.md)
