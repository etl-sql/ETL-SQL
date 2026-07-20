# CONVERT FILE ENCODING
Performs stream-based transcoding from one encoding standard to another.

## Syntax
```sql
CONVERT FILE ENCODING '<source>' TO '<destination>' WITH(FROM_ENCODING = '<enc>', TO_ENCODING = '<enc>' [, OVERWRITE = ON|OFF]);
```

## Example
```sql
-- Normalise a legacy ANSI extract to UTF-8 before loading
CONVERT FILE ENCODING 'landing/legacy.csv' TO 'staging/legacy_utf8.csv' WITH (
  FROM_ENCODING = 'ANSI',
  TO_ENCODING   = 'UTF8'
);
```

## Options
| Option | Description | Default |
| :--- | :--- | :--- |
| `FROM_ENCODING` | **Required.** Source encoding (e.g. `UTF8`, `ANSI`, `ASCII`, `UNICODE`, `UTF32`). | — |
| `TO_ENCODING` | **Required.** Target encoding. | — |
| `OVERWRITE` | Replace the destination if it already exists. | `ON` |

## References
- [Advanced File Operations](advanced-file-operations.md)
- [FILE Operations](file.md)
- [File Operations index](README.md)
