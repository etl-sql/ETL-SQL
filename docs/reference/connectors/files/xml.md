# XML

Document extraction with XPath addressing for nested elements. When querying an `XML` connection via
`SELECT`, the table name is `FILE`. Use `ROOT_PATH` to select the repeating element to unpack as rows.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | XPath to the repeating element (e.g. `/Catalog/Book`) | No |
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

XML file operations use local file system permissions or storage connector credentials.

## Examples

```sql
-- XPath root selector
CREATE CONNECTION xml_src AS XML('C:\Data\catalog.xml', ROOT_PATH='/Catalog/Product');

-- Easy button: compress (ZIP) then encrypt on write, decrypt then unzip on read
CREATE CONNECTION xml_vault AS XML(PATH='C:\Vault\archive.xml', COMPRESS=ON, ENCRYPT=ON, PASSWORD='SECRET:vault/xml_key');
INSERT INTO xml_vault.ROOT SELECT * FROM #catalog;
SELECT * FROM xml_vault.ROOT;
```

## Troubleshooting

- **XPath / Root Element**: Configure `ROW_TAG` to designate repeating record elements.
- **Malformed XML**: Verify entity encoding and XML closing tags.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [JSON](json.md)
- [Transactional File Writes](transactional-writes.md)
