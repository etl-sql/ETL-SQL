# PARQUET

Apache Parquet columnar format. Ideal for high-throughput analytics and interoperability with Spark,
Hive, and data-lake systems — it compresses well and supports efficient columnar reads.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `COMPRESSION` | `SNAPPY` (default), `GZIP`, `LZO`, `BROTLI`, `LZ4`, `ZSTD`, `UNCOMPRESSED` | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |
| `TRANSACTIONAL` | `ON`/`OFF` — stage the complete output beside the target and publish with one replacement rename (default: `OFF`) | No |

## Examples

```sql
-- Write a Snappy-compressed Parquet file (default)
CREATE CONNECTION pq_out AS PARQUET(PATH='C:\Data\output.parquet');

-- Maximum compression for archival
CREATE CONNECTION pq_archive AS PARQUET('C:\Archive\data.parquet', COMPRESSION=ZSTD);
```

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [Avro](avro.md)
- [Transactional File Writes](transactional-writes.md)
