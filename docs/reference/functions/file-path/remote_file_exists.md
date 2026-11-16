# REMOTE_FILE_EXISTS

Checks whether a file (or, for backward compatibility, a directory) exists on a remote connection (SFTP, FTP, S3, Azure Blob, or SharePoint). Prefer [REMOTE_DIRECTORY_EXISTS](remote_directory_exists.md) when checking specifically for a directory.

## Syntax

```sql
REMOTE_FILE_EXISTS(connection, path)
```

## Parameters

- **connection** - Configured remote connection name.
- **path** - Remote file or directory path to check.

## Returns

Returns `1` when the remote resource exists; otherwise returns `0`.

## Null Behavior

Returns `0` when the path does not exist. Returns `NULL` when required arguments are `NULL`.

## Examples

```sql
SELECT REMOTE_FILE_EXISTS(MyFtp, 'uploads/data.csv') AS upload_exists;
```

```sql
IF REMOTE_FILE_EXISTS(SftpDrop, 'incoming/customers.csv') = 1
BEGIN
    RECEIVE FILE FROM 'incoming/customers.csv' TO 'landing/customers.csv' AT SftpDrop;
END;
```

## References

- [Functions](../README.md)
- [FILE_EXISTS](file_exists.md)
- [REMOTE_FILE_LIST](remote_file_list.md)
