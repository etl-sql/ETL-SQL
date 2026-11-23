# FILE_ACCESSED

Returns the last access timestamp of a local file.

## Syntax

```sql
FILE_ACCESSED(path)
```

## Parameters

- **path** — `STRING`. Local file path to inspect. Relative paths are resolved through the execution context.

## Returns

Returns a `DATETIME` containing the file's last access timestamp (`FileInfo.LastAccessTime`).

## Null Behavior

Returns `NULL` when `path` is `NULL` or the file does not exist.

## Remarks

The path is resolved and authorized through the engine path boundary before file metadata is read. Filesystem mount options (such as `noatime` or Windows `DisableLastAccess`) may affect how frequently the OS updates last access timestamps.

## Example

```sql
SELECT FILE_ACCESSED('C:\Data\input.csv') AS input_accessed_at;
```

## References

- [File and Path Functions](README.md)
- [FILE_CREATED](file_created.md)
- [FILE_MODIFIED](file_modified.md)
- [FILE_READONLY](file_readonly.md)
