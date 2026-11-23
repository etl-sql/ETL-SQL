# DIRECTORY_SIZE

Returns the total size in bytes of files inside a local directory.

## Syntax

```sql
DIRECTORY_SIZE(path [, recursive])
```

## Parameters

- **path** — `STRING`. Local directory path to inspect. Relative paths are resolved through the execution context.
- **recursive** — Optional `BOOLEAN` flag (`TRUE` / `FALSE`). When `TRUE`, includes files in subdirectories. Defaults to `FALSE`.

## Returns

Returns a `DECIMAL` containing the total size in bytes (`0` for an empty directory).

## Null Behavior

Returns `NULL` when `path` is `NULL`, `recursive` is `NULL`, or the directory does not exist.

## Remarks

- The path is resolved and authorized through the engine path boundary before enumeration.
- Each enumerated file counts toward the script's file-operation limit (`SET ALLOW_FILE_OPERATIONS`), and recursive traversal honors the recursion depth limit (`SET ALLOW_RECURSIVE_LAYERS`, default 5 levels).

## Example

```sql
SELECT DIRECTORY_SIZE('C:\Data\Incoming', TRUE) AS total_bytes;
```

## References

- [File and Path Functions](README.md)
- [DIRECTORY_FILE_COUNT](directory_file_count.md)
- [DIRECTORY_EXISTS](directory_exists.md)
- [FILE_LIST](file_list.md)
