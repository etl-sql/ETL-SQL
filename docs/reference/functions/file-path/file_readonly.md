# FILE_READONLY

Returns whether a local file has the read-only attribute set.

## Syntax

```sql
FILE_READONLY(path)
```

## Parameters

- **path** — `STRING`. Local file path to inspect. Relative paths are resolved through the execution context.

## Returns

Returns `1` when the file is read-only and `0` when it is writable. Agrees with the `IsReadOnly` column exposed by the `DIRECTORY` connector (`FileInfo.IsReadOnly`).

## Null Behavior

Returns `NULL` when `path` is `NULL` or the file does not exist.

## Remarks

The path is resolved and authorized through the engine path boundary before file metadata is read.

## Example

```sql
SELECT FILE_READONLY('C:\Data\archive.csv') AS is_readonly;
```

## References

- [File and Path Functions](README.md)
- [FILE_ACCESSED](file_accessed.md)
- [FILE_CREATED](file_created.md)
- [FILE_MODIFIED](file_modified.md)
