# FILE_HASH
Computes the cryptographic checksum (hash) of a local file.

**Category:** File / Metadata

## Syntax
```sql
FILE_HASH(path [, algorithm])
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `path` | `VARCHAR` / `STRING` | The local path of the file |
| `algorithm` | `VARCHAR` / `STRING` | (Optional) The hash algorithm to use: `MD5`, `SHA1`, `SHA256`, or `SHA512`. Defaults to `SHA256`. |

## Returns
`STRING` — The computed hexadecimal hash string in lowercase. Returns `NULL` if the file does not exist, or if path is `NULL`.

## Example
```sql
SELECT FILE_HASH('C:\Data\input.csv', 'SHA256');  -- → 'a3ef...'
```

## See Also
- [Standard Library — §8.1 File Operations](../../../guides/getting-started.md#81-file-operations)
- Related: [`FILE_SIZE`](file_size.md), [`FILE_MODIFIED`](file_modified.md), [`HASHBYTES`](../cryptography/hashbytes.md)
