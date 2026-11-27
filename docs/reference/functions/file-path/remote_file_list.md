# REMOTE_FILE_LIST

Returns a table of files on a remote connection (SFTP, FTP, or Azure Blob).

## Syntax

```sql
REMOTE_FILE_LIST(connection_name)
REMOTE_FILE_LIST(connection_name, path)
REMOTE_FILE_LIST(connection_name, path, recursive)
```

## Parameters

- **connection_name** - Name of an SFTP, FTP, S3, Azure Blob, or SharePoint connection.
- **path** - Optional remote directory path to list. Defaults to the connection root.
- **recursive** - Optional boolean (`TRUE`/`FALSE`, `'ON'`/`'OFF'`, `1`/`0`, default `FALSE`). When enabled, traverses subdirectories up to `SET ALLOW_RECURSIVE_LAYERS` (default `5`), failing instead of truncating if the tree is deeper, and never follows symlinked folders.

## Returns

Returns a table with these columns: `Name`, `FullPath`, `Size`, `LastModified`, `IsDirectory`, `CreationTime`, `Permissions`, `Owner`, and `IsSymlink`.

### Connector Metadata Matrix

| Connector | `CreationTime` | `Permissions` | `Owner` | `IsSymlink` |
| :--- | :--- | :--- | :--- | :--- |
| `SFTP` | `NULL` (SFTP v3 has no birth time) | POSIX mode string (e.g. `rwxr-x---`) | Numeric UID string | `TRUE` / `FALSE` |
| `FTP` | Server `MLSD` `create` fact when reported, else `NULL` | POSIX mode string when reported, else `NULL` | Raw owner when reported, else `NULL` | `TRUE` / `FALSE` |
| `S3` | `NULL` | `NULL` | Bucket owner display name or ID when returned, else `NULL` | `NULL` |
| `AZURE_BLOB` | Blob `CreatedOn` timestamp | `NULL` | `NULL` | `NULL` |
| `SHAREPOINT` | `TimeCreated` timestamp when returned, else `NULL` | `NULL` | `NULL` | `NULL` |

## Null Behavior

Returns no rows when `path` is `NULL` or the remote directory cannot be listed.

## Examples

```sql
CREATE CONNECTION sftp_src AS SFTP(HOST='files.partner.com', USER='etl', KEYFILE='C:\keys\sftp.pem');

SELECT NAME, SIZE, LASTMODIFIED
INTO #remote_files
FROM REMOTE_FILE_LIST(sftp_src, '/var/ftp/incoming/')
WHERE NAME LIKE '%.csv' AND LASTMODIFIED > DATEADD(DAY, -1, GETDATE());
```

## References

- [Functions](../README.md)
- [Administration Guide](../../../administration/platform/README.md)
- [FILE_LIST](file_list.md)
