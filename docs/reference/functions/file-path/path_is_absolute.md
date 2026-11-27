# PATH_IS_ABSOLUTE

Returns whether a path is fully qualified (absolute) rather than relative or drive-relative.

## Syntax

```sql
PATH_IS_ABSOLUTE(path)
```

## Parameters

- **path** — `STRING`. File or directory path.

## Returns

Returns `1` when `path` is fully qualified (for example `C:\Data\orders.csv` or `\\server\share\orders.csv` on Windows, `/var/data/orders.csv` on Linux/macOS), and `0` when `path` is relative, drive-relative (such as `\data` or `C:file.csv` on Windows), or empty.

## Null Behavior

Returns `NULL` when `path` is `NULL`.

## Remarks

Pure string path check backed by `Path.IsPathFullyQualified`; performs no filesystem I/O and does not resolve or authorize the path.

## Example

```sql
SELECT PATH_IS_ABSOLUTE('relative/orders.csv') AS is_abs;
```

## References

- [File and Path Functions](README.md)
- [PATH_ROOT](path_root.md)
- [PATH_DIRECTORY](path_directory.md)
- [PATH_COMBINE](path_combine.md)
