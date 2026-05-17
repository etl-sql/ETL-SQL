# REMOTE_FILE_LIST
Returns a table of files on a remote connection (SFTP, FTP, or Azure Blob).

**Category:** File

## Syntax
```sql
REMOTE_FILE_LIST(connection_name)
REMOTE_FILE_LIST(connection_name, path)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `connection_name` | `IDENTIFIER` | Name of an SFTP, FTP, or AZURE_BLOB connection |
| `path` | `STRING` | Optional: remote directory path to list (default: connection root) |

## Returns
Table with columns: `NAME` (STRING), `FULLPATH` (STRING), `SIZE` (DECIMAL bytes), `LASTMODIFIED` (DATETIME), `ISDIRECTORY` (BIT).

## Example
```sql
CREATE CONNECTION sftp_src ON SFTP() WITH (HOST='files.partner.com', USER='etl', KEYFILE='C:\keys\sftp.pem');

SELECT NAME, SIZE, LASTMODIFIED
INTO #remote_files
FROM REMOTE_FILE_LIST(sftp_src, '/var/ftp/incoming/')
WHERE NAME LIKE '%.csv' AND LASTMODIFIED > DATEADD(DAY, -1, GETDATE());
```

## See Also
- [Standard Library — §14. File System Functions](../../../../../Docs/Reference/Standard_Library.md#14-file-system-functions)
- [Data Connectors Reference](../../../../../Docs/Reference/Data_Connectors.md)
- Related: [`FILE_LIST`](FILE_LIST.md)
