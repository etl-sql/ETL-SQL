# FTP

Legacy File Transfer Protocol. Supports active and passive mode depending on the server. Used with
`SEND FILE` / `RECEIVE FILE` and remote file operations, not `SELECT`/`INSERT`.

Aliases: `FTP_CONN`

> [!NOTE]
> `FTPS` (FTP over SSL/TLS) is treated as an alias token at parse time but uses the same connector.
> Provide `USE_SSL=TRUE` in the connection string if your server requires implicit FTPS.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | FTP server address or IP | Yes (structured) |
| `PORT` | Listening port (default: `21`) | No |
| `USER` | Login username | No |
| `PASSWORD` | Login password | No |

## Examples

```sql
-- Structured
CREATE CONNECTION ftp_src AS FTP(HOST='ftp.example.com', USER='ftpuser', PASSWORD='ftppass');

-- Traditional
CREATE CONNECTION ftp_legacy AS FTP('ftp.example.com', USER='ftpuser', PASSWORD='ftppass');
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [SFTP](sftp.md) · [TRANSFER](../../file-operations/transfer.md)
