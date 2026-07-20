# SPLIT FILE
Splits a larger text file into multiple chunk files based on row count or byte size.

## Syntax
```sql
SPLIT FILE '<source>' TO '<destination_dir>' WITH(LIMIT_TYPE = 'ROWS'|'SIZE', LIMIT_VALUE = <val> [, PREFIX = '<prefix>', OVERWRITE = ON|OFF]);
```

## Example
```sql
-- Break a large extract into 100k-row parts for parallel loading
SPLIT FILE 'landing/orders.csv' TO 'staging/parts/' WITH (
  LIMIT_TYPE  = 'ROWS',
  LIMIT_VALUE = 100000,
  PREFIX      = 'orders_'
);

-- Or split by size
SPLIT FILE 'landing/orders.csv' TO 'staging/parts/' WITH (
  LIMIT_TYPE  = 'SIZE',
  LIMIT_VALUE = '50MB'
);
```

## Options
| Option | Description | Default |
| :--- | :--- | :--- |
| `LIMIT_TYPE` | **Required.** Split strategy, `ROWS` or `SIZE`. | — |
| `LIMIT_VALUE` | **Required.** Number of rows, or a size limit such as `50MB` / `100KB`. | — |
| `PREFIX` | Name prefix for generated part files. | `part_` |
| `OVERWRITE` | Replace existing part files in the destination directory. | `ON` |

## References
- [Advanced File Operations](advanced-file-operations.md)
- [MERGE FILES](merge-files.md)
- [File Operations index](README.md)
