# VERIFY FILE INTEGRITY
Computes file hashes and validates them against expected hex strings or a companion checksum file.

## Syntax
```sql
VERIFY FILE INTEGRITY '<source>' WITH(EXPECTED_HASH = '<hash>' | HASH_FILE = '<path>' [, ALGORITHM = 'SHA256'|'SHA1'|'MD5'|'SHA512']);
```

## Example
```sql
-- Validate a download against its published checksum file
VERIFY FILE INTEGRITY 'landing/vendor_feed.zip' WITH (
  HASH_FILE = 'landing/vendor_feed.zip.sha256',
  ALGORITHM = 'SHA256'
);

-- Or against a known hash
VERIFY FILE INTEGRITY 'landing/vendor_feed.zip' WITH (
  EXPECTED_HASH = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
);
```

## Options
| Option | Description | Default |
| :--- | :--- | :--- |
| `EXPECTED_HASH` | A direct expected hash string. One of `EXPECTED_HASH` or `HASH_FILE` is required. | — |
| `HASH_FILE` | Path to a companion checksum file (e.g. `.sha256`). | — |
| `ALGORITHM` | Hash computation algorithm: `SHA256`, `SHA1`, `MD5`, or `SHA512`. | `SHA256` |

## References
- [Advanced File Operations](advanced-file-operations.md)
- [FILE Operations](file.md)
- [File Operations index](README.md)
