# MERGE FILES
Concatenates multiple files (supports wildcards or array inputs) into a single destination file.

## Syntax
```sql
MERGE FILES '<source_pattern>' TO '<destination>' [WITH(HEADER = ON|OFF, OVERWRITE = ON|OFF)];
```

## Example
```sql
-- Recombine split parts, keeping only the first header row
MERGE FILES 'staging/parts/orders_*.csv' TO 'staging/orders_all.csv' WITH (
  HEADER = ON
);
```

## Options
| Option | Description | Default |
| :--- | :--- | :--- |
| `HEADER` | When `ON`, treats the files as CSVs and strips the header row from every file after the first. | `ON` |
| `OVERWRITE` | Overwrite the destination file if it exists. | `ON` |

## References
- [Advanced File Operations](advanced-file-operations.md)
- [SPLIT FILE](split-file.md)
- [File Operations index](README.md)
