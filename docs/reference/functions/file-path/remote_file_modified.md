# REMOTE_FILE_MODIFIED

Returns the last modified timestamp of a file on a remote connection (SFTP, FTP, S3, Azure Blob, or SharePoint).

## Syntax

```sql
REMOTE_FILE_MODIFIED(connection, path)
```

## Parameters

- **connection** — `STRING` or connection identifier. Configured remote connection name.
- **path** — `STRING`. Remote file path to inspect.

## Returns

Returns a `DATETIME` containing the remote file's last modified timestamp.

## Null Behavior

Returns `NULL` when `connection` or `path` is `NULL`, when the remote file does not exist, or when the remote server does not report a modification timestamp.

## Remarks

Looks up the file via the connection's `IRemoteFileSystem` metadata listing after checking existence. Raises an error if the connection does not exist or does not support remote file operations.

## Example

```sql
SELECT REMOTE_FILE_MODIFIED(SftpDrop, 'incoming/orders.csv') AS modified_at;
```

## References

- [File and Path Functions](README.md)
- [REMOTE_FILE_SIZE](remote_file_size.md)
- [REMOTE_FILE_EXISTS](remote_file_exists.md)
- [FILE_MODIFIED](file_modified.md)
