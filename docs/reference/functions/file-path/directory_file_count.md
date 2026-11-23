# DIRECTORY_FILE_COUNT

Returns the number of files inside a local directory.

## Syntax

```sql
DIRECTORY_FILE_COUNT(path [, recursive])
```

## Parameters

- **path** — `STRING`. Local directory path to inspect. Relative paths are resolved through the execution context.
- **recursive** — Optional `BOOLEAN` flag (`TRUE` / `FALSE`). When `TRUE`, includes files in subdirectories. Defaults to `FALSE`.

## Returns

Returns a `DECIMAL` integer containing the file count (`0` for an empty directory).

## Null Behavior

Returns `NULL` when `path` is `NULL`, `recursive` is `NULL`, or the directory does not exist.

## Remarks

- The path is resolved and authorized through the engine path boundary before enumeration.
- Each enumerated file counts toward the script's file-operation limit (`SET ALLOW_FILE_OPERATIONS`), and recursive traversal honors the recursion depth limit (`SET ALLOW_RECURSIVE_LAYERS`, default 5 levels).

## Example

```sql
SELECT DIRECTORY_FILE_COUNT('C:\Data\Incoming', TRUE) AS total_files;
```

## References

- [File and Path Functions](README.md)
- [DIRECTORY_SIZE](directory_size.md)
- [DIRECTORY_EXISTS](directory_exists.md)
- [FILE_LIST](file_list.md)
