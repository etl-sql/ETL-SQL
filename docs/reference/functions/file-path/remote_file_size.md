# REMOTE_FILE_SIZE

Returns the size in bytes of a file on a remote connection (SFTP, FTP, S3, Azure Blob, or SharePoint).

## Syntax

```sql
REMOTE_FILE_SIZE(connection, path)
```

## Parameters

- **connection** — `STRING` or connection identifier. Configured remote connection name.
- **path** — `STRING`. Remote file path to inspect.

## Returns

Returns the file size in bytes as a `DECIMAL` (numeric) value.

## Null Behavior

Returns `NULL` when `connection` or `path` is `NULL`, or when the remote file does not exist.

## Remarks

Looks up the file via the connection's `IRemoteFileSystem` metadata listing after checking existence. Raises an error if the connection does not exist or does not support remote file operations.

## Example

```sql
SELECT REMOTE_FILE_SIZE(SftpDrop, 'incoming/orders.csv') AS remote_bytes;
```

## References

- [File and Path Functions](README.md)
- [REMOTE_FILE_MODIFIED](remote_file_modified.md)
- [REMOTE_FILE_EXISTS](remote_file_exists.md)
- [REMOTE_FILE_LIST](remote_file_list.md)
