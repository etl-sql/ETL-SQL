# FILE_MODIFIED

Returns the last modification (write) timestamp of a local file.

## Syntax

```sql
FILE_MODIFIED(path)
```

## Parameters

- **path** - Local file path to inspect.

## Returns

Returns a `DATETIME` containing the file's last write timestamp.

## Null Behavior

Returns `NULL` when `path` is `NULL` or the file does not exist.

## Remarks

The path is resolved through the engine path boundary before file metadata is read.

## Examples

```sql
SELECT FILE_MODIFIED('C:\Data\input.csv') AS input_modified_at;
```

```sql
SELECT source_path, FILE_MODIFIED(source_path) AS modified_at
FROM #file_inventory;
```

## References

- [Standard Library](../standard-library.md)
- [FILE_SIZE](file_size.md)
- [FILE_HASH](file_hash.md)
