# DIRECTORY_EXISTS

Returns whether a local directory exists inside the active execution context's allowed path boundaries.

## Syntax

```sql
DIRECTORY_EXISTS(path)
```

## Parameters

- **path** - Directory path to test. Relative paths are resolved through the execution context.

## Returns

Returns `1` when the directory exists and `0` when it does not.

## Null Behavior

`DIRECTORY_EXISTS(NULL)` returns `0`.

## Security Notes

- Directory paths are subject to ETL-SQL path boundary checks.
- Avoid drive roots and system directories.
- Use this function before directory operations that depend on a path being present.

## Examples

```sql
IF DIRECTORY_EXISTS('inbound') = 0
BEGIN
  CREATE DIRECTORY 'inbound';
END;
```

```sql
SELECT DIRECTORY_EXISTS('archive') AS archive_ready;
```

## References

- [File Operations](../../file-operations/README.md)
- [DIRECTORY](directory.md)
- [FILE_EXISTS](file_exists.md)
