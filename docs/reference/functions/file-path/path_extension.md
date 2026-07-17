# PATH_EXTENSION

Extracts the extension portion of a path (including the period `.`).

## Syntax

```sql
PATH_EXTENSION(path)
```

## Parameters

- **path** - File path.

## Returns

Returns the extension, including the period. Returns an empty string when there is no extension.

## Null Behavior

Returns `NULL` when `path` is `NULL`.

## Examples

```sql
SELECT PATH_EXTENSION('C:\Data\input.csv') AS extension;
```

```sql
SELECT *
FROM #files
WHERE PATH_EXTENSION(file_path) = '.csv';
```

## References

- [Functions](../README.md)
- [PATH_COMBINE](path_combine.md)
- [PATH_FILENAME](path_filename.md)
- [PATH_DIRECTORY](path_directory.md)
