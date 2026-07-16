# FILE_HASH

Computes the cryptographic checksum (hash) of a local file.

## Syntax

```sql
FILE_HASH(path)
FILE_HASH(path, algorithm)
```

## Parameters

- **path** - Local file path.
- **algorithm** - Optional hash algorithm: `MD5`, `SHA1`, `SHA256`, or `SHA512`. Defaults to `SHA256`.

## Returns

Returns the computed hexadecimal hash string in lowercase.

## Null Behavior

Returns `NULL` when `path` is `NULL` or the file does not exist.

## Remarks

Local file access must pass the engine path-resolution guardrails.

## Examples

```sql
SELECT FILE_HASH('C:\Data\input.csv', 'SHA256') AS file_sha256;
```

```sql
SELECT file_path, FILE_HASH(file_path) AS content_hash
FROM #incoming_files;
```

## References

- [Standard Library](../standard-library.md)
- [FILE_SIZE](file_size.md)
- [FILE_MODIFIED](file_modified.md)
- [HASHBYTES](../cryptography/hashbytes.md)
