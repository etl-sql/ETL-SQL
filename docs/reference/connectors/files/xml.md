# XML

Document extraction with XPath addressing for nested elements. When querying an `XML` connection via
`SELECT`, the table name is `FILE`. Use `ROOT_PATH` to select the repeating element to unpack as rows.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | XPath to the repeating element (e.g. `/Catalog/Book`) | No |
| `ENCODING` | Character encoding (default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — transparent GZip support | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

## Examples

```sql
-- XPath root selector
CREATE CONNECTION xml_src AS XML('C:\Data\catalog.xml', ROOT_PATH='/Catalog/Product');

-- Encrypted XML archive
CREATE CONNECTION xml_vault AS XML(PATH='C:\Vault\archive.xml', ENCRYPT=ON, PASSWORD='vault_pass');
```

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [JSON](json.md)
