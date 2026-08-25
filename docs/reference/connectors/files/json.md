# JSON

Document extraction with JSONPath addressing for nested data. When querying a `JSON` connection via
`SELECT`, the table name is `FILE`. Use `ROOT_PATH` to navigate to the array node to unpack as rows.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | JSONPath to the root data array (e.g. `$.data.orders`) | No |
| `ENCODING` | Character encoding (default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — transparent GZip support | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |
| `TRANSACTIONAL` | `ON`/`OFF` — stage the complete output beside the target and publish with one replacement rename (default: `OFF`) | No |

## Authentication

JSON file operations use local file system permissions or storage connector credentials.

## Examples

```sql
-- Drill into a nested array
CREATE CONNECTION json_src AS JSON('C:\Data\orders.json', ROOT_PATH='$.data.orders');

-- Compressed JSON
CREATE CONNECTION json_gz AS JSON(PATH='C:\Data\events.json.gz', COMPRESS=ON);
```

## Troubleshooting

- **Parse Error**: Ensure file is valid JSON (array of objects) or NDJSON (one JSON object per line).
- **Nested Schema**: Use `UNNEST` or JSON functions to flatten nested arrays.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [XML](xml.md)
- [Transactional File Writes](transactional-writes.md)
