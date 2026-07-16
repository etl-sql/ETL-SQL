# REMOTE_FILE_LIST

Returns a table of files on a remote connection (SFTP, FTP, or Azure Blob).

## Syntax

```sql
REMOTE_FILE_LIST(connection_name)
REMOTE_FILE_LIST(connection_name, path)
```

## Parameters

- **connection_name** - Name of an SFTP, FTP, or Azure Blob connection.
- **path** - Optional remote directory path to list. Defaults to the connection root.

## Returns

Returns a table with these columns: `NAME`, `FULLPATH`, `SIZE`, `LASTMODIFIED`, and `ISDIRECTORY`.

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

- [Standard Library](../standard-library.md)
- [Administration Guide](../../../guides/administration.md)
- [FILE_LIST](file_list.md)
