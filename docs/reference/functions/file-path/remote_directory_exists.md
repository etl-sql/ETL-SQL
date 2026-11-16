# REMOTE_DIRECTORY_EXISTS

Checks whether a directory exists on a remote connection (SFTP, FTP, S3, Azure Blob, or SharePoint).

## Syntax

```sql
REMOTE_DIRECTORY_EXISTS(connection, path)
```

## Parameters

- **connection** — `STRING` or connection identifier. Configured remote connection name.
- **path** — `STRING`. Remote directory path to check.

## Returns

Returns `1` when the remote directory exists; otherwise returns `0`.

## Null Behavior

Returns `NULL` when `connection` or `path` is `NULL`.

## Remarks

Unlike `REMOTE_FILE_EXISTS`, `REMOTE_DIRECTORY_EXISTS` checks specifically for directories and returns `0` for regular files.

## Examples

```sql
SELECT REMOTE_DIRECTORY_EXISTS(SftpDrop, 'incoming/2026-11') AS dir_exists;
```

```sql
IF REMOTE_DIRECTORY_EXISTS(SftpDrop, 'incoming/2026-11') = 1
BEGIN
    SEND FILE 'staging/orders.csv' TO 'incoming/2026-11/orders.csv' AT SftpDrop;
END;
```

## References

- [File and Path Functions](README.md)
- [REMOTE_FILE_EXISTS](remote_file_exists.md)
- [DIRECTORY_EXISTS](directory_exists.md)
- [REMOTE_FILE_LIST](remote_file_list.md)
