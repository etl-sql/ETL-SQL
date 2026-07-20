# SYNC DIRECTORY
Mirrors a source directory to a destination directory, doing fast file transfers based on modified
times and sizes.

## Syntax
```sql
SYNC DIRECTORY '<source_dir>' TO '<destination_dir>' [WITH(DELETE_EXTRA = ON|OFF, OVERWRITE = ON|OFF, RECURSIVE = ON|OFF)];
```

## Example
```sql
CREATE CONNECTION source_dir AS DIRECTORY('\fileserver\exports');
CREATE CONNECTION backup_dir AS DIRECTORY('D:\Backups\Daily');

SYNC DIRECTORY source_dir TO backup_dir WITH (
  RECURSIVE    = ON,
  DELETE_EXTRA = OFF
);
```

## Options
| Option | Description | Default |
| :--- | :--- | :--- |
| `DELETE_EXTRA` | Delete files in the destination that do not exist in the source. | `OFF` |
| `OVERWRITE` | Overwrite modified/changed files. | `ON` |
| `RECURSIVE` | Traverse directories recursively. | `OFF` |

> `DELETE_EXTRA = ON` removes destination files that are absent from the source. Confirm the
> direction of the sync before enabling it.

## References
- [Advanced File Operations](advanced-file-operations.md)
- [DIRECTORY Operations](directory.md)
- [File Operations index](README.md)
