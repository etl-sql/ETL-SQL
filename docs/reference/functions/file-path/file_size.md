# FILE_SIZE

Returns the size of a local file in bytes.

## Syntax

```sql
FILE_SIZE(path)
```

## Parameters

- **path** - Local file path to inspect.

## Returns

Returns the file size in bytes as a numeric value.

## Null Behavior

Returns `NULL` when `path` is `NULL` or the file does not exist.

## Remarks

The path is resolved through the engine path boundary before file metadata is read.

## Examples

```sql
SELECT FILE_SIZE('C:\Data\input.csv') AS input_bytes;
```

```sql
SELECT source_path, FILE_SIZE(source_path) AS size_bytes
FROM #file_inventory;
```

## References

- [Functions](../README.md)
- [FILE_HASH](file_hash.md)
- [FILE_MODIFIED](file_modified.md)
