# PARQUET

Apache Parquet columnar format. Ideal for high-throughput analytics and interoperability with Spark,
Hive, and data-lake systems — it compresses well and supports efficient columnar reads.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `COMPRESSION` | Native Parquet column compression: `SNAPPY` (default), `GZIP`, `LZO`, `BROTLI`, `LZ4`, `ZSTD`, `UNCOMPRESSED` | No |
| `ENCRYPT` | `ON`/`OFF`/`PASSWORD`/`KEYFILE`/`MACHINE`/`PORTAL` — encrypt on write and decrypt on read (default: `OFF`). Adds `.pgp` to the path | No |
| `PASSWORD` | Password for AES encryption/decryption (required for `ENCRYPT=ON`/`PASSWORD` when `KEYFILE` is not set) | Conditional |
| `ALGORITHM` | Key-derivation hash for password encryption: `SHA2_256`/`SHA256` (default) or `SHA2_512`/`SHA512` (`MD5` and `SHA1` are rejected) | No |
| `KEYFILE` | Path to SSH key file for key-pair encryption/decryption | Conditional |
| `PASSPHRASE` | Passphrase for an encrypted `KEYFILE` | Conditional |
| `TRANSACTIONAL` | `ON`/`OFF` — stage the complete output beside the target and publish with one replacement rename (default: `OFF`) | No |

> [!NOTE]
> **Write-time compression and encryption:** Parquet uses format-native column compression
> (`COMPRESSION`, default `SNAPPY`) rather than the `COMPRESS` ZIP wrapper. When `ENCRYPT=ON` is
> set, Parquet writes the compressed `.parquet` file first and then encrypts it (appending `.pgp`);
> reads decrypt first and then decode Parquet. `PASSWORD`, `KEYFILE` (+ optional `PASSPHRASE`),
> `ENCRYPT=MACHINE`, and `ENCRYPT=PORTAL` are mutually exclusive key sources (`MACHINE` takes
> precedence over `KEYFILE`, and `KEYFILE` takes precedence over `PASSWORD`). Enabling encryption
> without a key source fails closed.

## Authentication

Parquet file operations use local file system permissions or storage connector credentials.

## Examples

```sql
-- Write a Snappy-compressed Parquet file (default)
CREATE CONNECTION pq_out AS PARQUET(PATH='C:\Data\output.parquet');

-- Easy button: native ZSTD compression then encrypt on write, decrypt then read back
CREATE CONNECTION pq_archive AS PARQUET(PATH='C:\Archive\data.parquet', COMPRESSION=ZSTD, ENCRYPT=ON, PASSWORD='SECRET:archive/pq_key');
INSERT INTO pq_archive.FILE SELECT * FROM #events;
SELECT * FROM pq_archive.FILE;
```

## Troubleshooting

- **Type Mismatch**: Ensure column data types match Parquet schema types.
- **Compression Unsupported**: Supported codecs include SNAPPY, GZIP, ZSTD, and UNCOMPRESSED.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [Avro](avro.md)
- [Transactional File Writes](transactional-writes.md)
