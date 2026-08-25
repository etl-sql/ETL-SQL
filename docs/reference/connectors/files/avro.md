# AVRO

Apache Avro format. The schema is embedded within the file; optionally reference an external `.avsc`
schema file.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `SCHEMA_FILE` | Path to an external `.avsc` Avro schema file | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

## Authentication

Avro file operations use file system permissions or storage connector credentials (S3, Azure Blob, SFTP).

## Examples

```sql
-- Read Avro with an external schema definition
CREATE CONNECTION avro_src AS AVRO('C:\Data\events.avro', SCHEMA_FILE='C:\Schemas\events.avsc');
```

## Troubleshooting

- **Schema Evolution Failure**: Verify writer schema is compatible with reader schema.
- **Corrupt File**: Ensure file was not truncated during transfer.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [Parquet](parquet.md)
