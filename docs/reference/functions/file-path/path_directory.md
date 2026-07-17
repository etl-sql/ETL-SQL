# PATH_DIRECTORY

Extracts the directory information portion of a path.

## Syntax

```sql
PATH_DIRECTORY(path)
```

## Parameters

- **path** - File or directory path.

## Returns

Returns the directory path.

## Null Behavior

Returns `NULL` when `path` is `NULL` or contains no directory information.

## Examples

```sql
SELECT PATH_DIRECTORY('C:\Data\SubDir\input.csv') AS directory_path;
```

```sql
SELECT PATH_DIRECTORY(file_path) AS parent_directory
FROM #files;
```

## References

- [Standard Library](../standard-library.md)
- [PATH_COMBINE](path_combine.md)
- [PATH_FILENAME](path_filename.md)
- [PATH_EXTENSION](path_extension.md)
