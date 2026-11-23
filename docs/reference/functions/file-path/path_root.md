# PATH_ROOT

Extracts the root directory portion of a file or directory path.

## Syntax

```sql
PATH_ROOT(path)
```

## Parameters

- **path** — `STRING`. File or directory path.

## Returns

Returns the root portion of `path` as a `STRING` (for example `C:\` or `\\server\share` on Windows, `/` on Linux/macOS), or an empty string `''` when `path` is relative or empty.

## Null Behavior

Returns `NULL` when `path` is `NULL`.

## Remarks

Pure string path manipulation backed by `Path.GetPathRoot`; performs no filesystem I/O and does not resolve or authorize the path.

## Example

```sql
SELECT PATH_ROOT('C:\Data\orders.csv') AS root_path;
```

## References

- [File and Path Functions](README.md)
- [PATH_IS_ABSOLUTE](path_is_absolute.md)
- [PATH_DIRECTORY](path_directory.md)
- [PATH_COMBINE](path_combine.md)
