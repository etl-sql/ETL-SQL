# PATH_FILENAME_NO_EXT

Extracts the filename without its extension from a path.

## Syntax

```sql
PATH_FILENAME_NO_EXT(path)
```

## Parameters

- **path** — `STRING`. File or directory path.

## Returns

Returns the filename without the trailing extension as a `STRING`.

## Null Behavior

Returns `NULL` when `path` is `NULL`.

## Remarks

Pure string path manipulation; performs no filesystem I/O and does not check whether the path exists.

## Example

```sql
SELECT PATH_FILENAME_NO_EXT('C:\Data\orders.csv') AS stem;
```

## References

- [File and Path Functions](README.md)
- [PATH_FILENAME](path_filename.md)
- [PATH_EXTENSION](path_extension.md)
- [PATH_CHANGE_EXTENSION](path_change_extension.md)
