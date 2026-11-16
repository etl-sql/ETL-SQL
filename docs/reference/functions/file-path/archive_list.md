# ARCHIVE_LIST

Returns a table of entries inside a `.zip`, `.tar`, `.tar.gz` (`.tgz`), or `.gz` archive without extracting files to disk.

## Syntax

```sql
ARCHIVE_LIST(path)
```

## Parameters

- **path** — Local path to the archive file (`.zip`, `.tar`, `.tar.gz`, `.tgz`, or `.gz`).

## Returns

Returns a table with columns:

- **Name** (`STRING`) — Entry file or directory name.
- **FullPath** (`STRING`) — Normalized relative path inside the archive.
- **Size** (`DECIMAL`) — Uncompressed entry size in bytes (`NULL` for `.gz` single-file streams).
- **CompressedSize** (`DECIMAL`) — Compressed entry size in bytes (`NULL` for `.tar.gz` stream entries).
- **LastModified** (`DATETIME`) — Last modification timestamp of the entry.
- **IsDirectory** (`BOOLEAN`) — `TRUE` for directory entries, `FALSE` for files.

## Null Behavior

Returns an empty table when `path` is `NULL`. Throws an execution error if the archive file does not exist or cannot be read.

## Examples

```sql
SELECT Name, FullPath, Size, CompressedSize, IsDirectory
FROM ARCHIVE_LIST('C:\Inbound\vendor_drop.tar.gz')
WHERE IsDirectory = FALSE AND Name LIKE '%.csv'
ORDER BY FullPath;
```

## References

- [Functions](../README.md)
- [COMPRESS / DECOMPRESS FILE](../../file-operations/compress-file.md)
- [FILE_LIST](file_list.md)
- [Syntax Index](../../../syntax-index.md)
