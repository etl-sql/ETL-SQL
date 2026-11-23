# AVRO

Apache Avro format. The schema is embedded within the file; optionally reference an external `.avsc`
schema file.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `SCHEMA_FILE` | Path to an external `.avsc` Avro schema file | No |
| `ENCRYPT` | `ON`/`OFF`/`PASSWORD`/`KEYFILE`/`MACHINE`/`PORTAL` — encrypt on write and decrypt on read (default: `OFF`). Adds `.pgp` to the path | No |
| `PASSWORD` | Password for AES encryption/decryption (required for `ENCRYPT=ON`/`PASSWORD` when `KEYFILE` is not set) | Conditional |
| `ALGORITHM` | Key-derivation hash for password encryption: `SHA2_256`/`SHA256` (default) or `SHA2_512`/`SHA512` (`MD5` and `SHA1` are rejected) | No |
| `KEYFILE` | Path to SSH key file for key-pair encryption/decryption | Conditional |
| `PASSPHRASE` | Passphrase for an encrypted `KEYFILE` | Conditional |

> [!NOTE]
> **Write-time encryption:** `AVRO` does not support the `COMPRESS` ZIP wrapper option. When
> `ENCRYPT=ON` is set, writes stage the Avro file and then encrypt it (appending `.pgp`); reads
> decrypt first and then decode Avro. `PASSWORD`, `KEYFILE` (+ optional `PASSPHRASE`),
> `ENCRYPT=MACHINE`, and `ENCRYPT=PORTAL` are mutually exclusive key sources (`MACHINE` takes
> precedence over `KEYFILE`, and `KEYFILE` takes precedence over `PASSWORD`). Enabling encryption
> without a key source fails closed.

## Authentication

Avro file operations use file system permissions or storage connector credentials (S3, Azure Blob, SFTP).

## Examples

```sql
-- Read Avro with an external schema definition
CREATE CONNECTION avro_src AS AVRO('C:\Data\events.avro', SCHEMA_FILE='C:\Schemas\events.avsc');

-- Easy button: encrypt on write, decrypt on read
CREATE CONNECTION avro_secure AS AVRO(PATH='C:\Secure\events.avro', ENCRYPT=ON, PASSWORD='SECRET:events/avro_key');
INSERT INTO avro_secure.FILE SELECT * FROM #events;
SELECT * FROM avro_secure.FILE;
```

## Troubleshooting

- **Schema Evolution Failure**: Verify writer schema is compatible with reader schema.
- **Corrupt File**: Ensure file was not truncated during transfer.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [Parquet](parquet.md)
