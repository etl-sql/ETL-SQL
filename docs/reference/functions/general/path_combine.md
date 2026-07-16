# PATH_COMBINE

Combines multiple path segments into a single file/directory path securely.

## Syntax

```sql
PATH_COMBINE(path1, path2, ...)
```

## Parameters

- **path1** - First path segment.
- **path2** - Second path segment.
- **...** - Additional path segments.

## Returns

Returns the combined path string.

## Null Behavior

Skips `NULL` segments. Returns `NULL` when all segments are `NULL`.

## Examples

```sql
SELECT PATH_COMBINE('C:\Data', 'SubDir', 'file.txt') AS full_path;
```

```sql
SELECT PATH_COMBINE(root_path, file_name) AS output_path
FROM #exports;
```

## References

- [Standard Library](../standard-library.md)
- [PATH_FILENAME](path_filename.md)
- [PATH_EXTENSION](path_extension.md)
- [PATH_DIRECTORY](path_directory.md)
