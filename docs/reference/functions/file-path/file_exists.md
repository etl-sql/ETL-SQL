# FILE_EXISTS

Returns whether a local file exists inside the active execution context's allowed path boundaries.

## Syntax

```sql
FILE_EXISTS(path)
```

## Parameters

- **path** - File path to test. Relative paths are resolved through the execution context.

## Returns

Returns `1` when the file exists and `0` when it does not.

## Null Behavior

`FILE_EXISTS(NULL)` returns `0`.

## Security Notes

- File paths are subject to ETL-SQL path boundary checks.
- Script files such as `.sql`, `.etlsql`, and `.rptsql` are protected by the script immutability guardrail.
- Use this function before destructive file operations.

## Examples

```sql
IF FILE_EXISTS('inbound/customers.csv') = 1
BEGIN
  PRINT 'Customer file is ready.';
END;
```

```sql
IF FILE_EXISTS('archive/customers.csv') = 0
BEGIN
  COPY FILE 'inbound/customers.csv' TO 'archive/customers.csv';
END;
```

## References

- [File Operations](../../file-operations/README.md)
- [Functions](../README.md)
