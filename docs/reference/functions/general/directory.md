# DIRECTORY

Returns directory contents as rows. `DIRECTORY` is an alias for [`FILE_LIST`](file_list.md).

## Syntax

```sql
DIRECTORY(path [, recursive])
```

## Parameters

- **path** - Directory path to list. Relative paths are resolved through the execution context.
- **recursive** - Optional boolean flag that includes nested files when enabled.

## Returns

Returns a table of directory entries. Columns include file metadata such as name, path, size, and modified timestamp where available.

## Null Behavior

Returns no rows when `path` is `NULL`.

## Security Notes

- Directory access is subject to ETL-SQL path boundary checks.
- Do not point examples or scripts at drive roots or system directories.
- Recursive listing is subject to configured recursion and file-operation limits.

## Examples

```sql
SELECT *
FROM DIRECTORY('inbound');
```

```sql
SELECT path, size
FROM DIRECTORY('inbound', TRUE)
WHERE path LIKE '%.csv';
```

## References

- [Standard Library](../standard-library.md)
- [FILE_LIST](file_list.md)
- [DIRECTORY_EXISTS](directory_exists.md)
