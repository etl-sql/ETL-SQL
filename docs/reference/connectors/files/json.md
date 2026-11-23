# JSON

Document extraction with JSONPath addressing for nested data. When querying a `JSON` connection via
`SELECT`, the table name is `FILE`. Use `ROOT_PATH` to navigate to the array node to unpack as rows.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | Dot path to the data array when reading (`$.data.orders` or `data.orders`). Writes through a `ROOT_PATH` connection are refused | No |
| `ENCODING` | Character encoding (default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — store the file as a ZIP archive (one entry); `.zip` is added to the path if it does not end in `.zip` (default: `OFF`) | No |
| `ENCRYPT` | `ON`/`OFF`/`PASSWORD`/`KEYFILE`/`MACHINE`/`PORTAL` — encrypt on write and decrypt on read (default: `OFF`). Adds `.pgp` when `COMPRESS=OFF` or `.zip` when `COMPRESS=ON` | No |
| `PASSWORD` | Password for AES encryption/decryption (required for `ENCRYPT=ON`/`PASSWORD` when `KEYFILE` is not set) | Conditional |
| `ALGORITHM` | Key-derivation hash for password encryption: `SHA2_256`/`SHA256` (default) or `SHA2_512`/`SHA512` (`MD5` and `SHA1` are rejected) | No |
| `KEYFILE` | Path to SSH key file for key-pair encryption/decryption | Conditional |
| `PASSPHRASE` | Passphrase for an encrypted `KEYFILE` | Conditional |
| `TRANSACTIONAL` | `ON`/`OFF` — stage the complete output beside the target and publish with one replacement rename (default: `OFF`) | No |

> [!NOTE]
> **Write-time compression and encryption:** When both `COMPRESS=ON` and `ENCRYPT=ON` are set,
> writes always compress (ZIP) first and then encrypt; reads reverse the order (decrypt, then
> unzip). `PASSWORD`, `KEYFILE` (+ optional `PASSPHRASE`), `ENCRYPT=MACHINE`, and `ENCRYPT=PORTAL`
> are mutually exclusive key sources (`MACHINE` takes precedence over `KEYFILE`, and `KEYFILE` takes
> precedence over `PASSWORD`). Enabling encryption without a key source fails closed.

## Authentication

JSON file operations use local file system permissions or storage connector credentials.

## Examples

```sql
-- Drill into a nested array
CREATE CONNECTION json_src AS JSON('C:\Data\orders.json', ROOT_PATH='$.data.orders');

-- Easy button: compress (ZIP) then encrypt on write, decrypt then unzip on read
CREATE CONNECTION json_secure AS JSON(PATH='C:\Data\events.json', COMPRESS=ON, ENCRYPT=ON, PASSWORD='SECRET:events/file_key');
INSERT INTO json_secure.FILE SELECT * FROM #events;
SELECT * FROM json_secure.FILE;
```

## Troubleshooting

- **Parse Error**: Ensure file is valid JSON (array of objects) or NDJSON (one JSON object per line).
- **Nested Schema**: Use `UNNEST` or JSON functions to flatten nested arrays.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [XML](xml.md)
- [Transactional File Writes](transactional-writes.md)
