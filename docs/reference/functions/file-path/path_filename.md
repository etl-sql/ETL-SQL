# PATH_FILENAME

Extracts the filename and extension portion of a path.

## Syntax

```sql
PATH_FILENAME(path)
```

## Parameters

- **path** - File or directory path.

## Returns

Returns the filename and extension as a `STRING`.

## Null Behavior

Returns `NULL` when `path` is `NULL`.

## Examples

```sql
SELECT PATH_FILENAME('C:\Data\input.csv') AS file_name;
```

```sql
SELECT source_path, PATH_FILENAME(source_path) AS file_name
FROM #file_inventory;
```

## References

- [Standard Library](../standard-library.md)
- [PATH_COMBINE](path_combine.md)
- [PATH_EXTENSION](path_extension.md)
- [PATH_DIRECTORY](path_directory.md)
