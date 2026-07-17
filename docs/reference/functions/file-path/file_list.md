# FILE_LIST

Returns a table of files in a local directory matching optional criteria.

## Syntax

```sql
FILE_LIST(path)
FILE_LIST(path, recursive)
```

## Parameters

- **path** - Directory path to list.
- **recursive** - Optional flag. Use `TRUE` to include subdirectories recursively. Defaults to `FALSE`.

## Returns

Returns a table with these columns: `NAME`, `PATH`, `EXTENSION`, `SIZE`, `LASTMODIFIED`, `ISREADONLY`, and `CREATIONTIME`.

## Null Behavior

Returns no rows when `path` is `NULL` or the directory cannot be listed.

## Remarks

- `path` must pass the engine path-resolution guardrails.
- Use `WHERE EXTENSION = '.csv'` to filter by file type.

## Examples

```sql
SELECT NAME, SIZE, LASTMODIFIED
INTO #incoming
FROM FILE_LIST('C:\Data\Incoming\')
WHERE EXTENSION = '.csv'
ORDER BY LASTMODIFIED DESC;
```

```sql
SELECT *
FROM FILE_LIST('C:\Reports\', TRUE)
WHERE SIZE > 1048576;
```

## References

- [Standard Library](../standard-library.md)
- [Administration Guide](../../../guides/administration.md)
- [REMOTE_FILE_LIST](remote_file_list.md)
- [FILE_EXISTS](file_exists.md)
