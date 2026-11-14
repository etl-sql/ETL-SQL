# FILE_CREATED

Returns the creation timestamp of a local file.

## Syntax

```sql
FILE_CREATED(path)
```

## Parameters

- **path** — `STRING`. Local file path to inspect. Relative paths are resolved through the execution context.

## Returns

Returns a `DATETIME` containing the file's creation timestamp.

## Null Behavior

Returns `NULL` when `path` is `NULL` or the file does not exist.

## Remarks

The path is resolved and authorized through the engine path boundary before file metadata is read.

## Example

```sql
SELECT FILE_CREATED('C:\Data\input.csv') AS input_created_at;
```

## References

- [File and Path Functions](README.md)
- [FILE_MODIFIED](file_modified.md)
- [FILE_SIZE](file_size.md)
