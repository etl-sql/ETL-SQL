# FTP

File Transfer Protocol, with optional TLS (FTPS). Used with `SEND FILE` / `RECEIVE FILE` and remote
file operations, not `SELECT`/`INSERT`.

Aliases: `FTP_CONN`

> [!IMPORTANT]
> Plain FTP sends the user name, password and files unencrypted. Set `USE_SSL` whenever the server
> supports TLS. There is no separate `FTPS` connection type; FTPS is `FTP` with `USE_SSL`.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | FTP server address or IP | Yes (structured) |
| `PORT` | Control port (default: `21`; implicit FTPS servers usually listen on `990`) | No |
| `USER` | Login username | No |
| `PASSWORD` | Login password | No |
| `USE_SSL` | `OFF` (default), `EXPLICIT` or `IMPLICIT`. `ON` and `TRUE` mean `EXPLICIT`. Any other value is an error and the connection does not open. | No |
| `PASSIVE` | `ON` (default) or `OFF` for active mode. Any other value is an error. | No |

`EXPLICIT` connects in plain text and upgrades with `AUTH TLS` before logging in, which is what most
FTPS servers expect on port 21. `IMPLICIT` starts TLS immediately and is for older servers on a
dedicated port, usually 990.

## Authentication

FTP connector supports:
- **Standard Credentials**: Set `USER` and `PASSWORD`.
- **Anonymous**: Set `USER='anonymous'` and email address as password.

## Examples

```sql
-- Structured
CREATE CONNECTION ftp_src AS FTP(HOST='ftp.example.com', USER='ftpuser', PASSWORD='ftppass');

-- Traditional
CREATE CONNECTION ftp_legacy AS FTP('ftp.example.com', USER='ftpuser', PASSWORD='ftppass');

-- FTPS: TLS before the password is sent
CREATE CONNECTION ftps_src AS FTP(HOST='ftp.example.com', USER='ftpuser', PASSWORD='ftppass', USE_SSL='EXPLICIT');

-- Implicit FTPS on its dedicated port, active mode
CREATE CONNECTION ftps_old AS FTP(HOST='ftp.example.com', PORT=990, USER='ftpuser', PASSWORD='ftppass',
    USE_SSL='IMPLICIT', PASSIVE='OFF');
```

## Troubleshooting

- **Passive mode**: passive mode is the default and works behind most NAT and firewalls. Set
  `PASSIVE='OFF'` only when the server requires active mode.
- **TLS errors**: if `EXPLICIT` fails during the handshake, the server may expect `IMPLICIT` on port
  990. If the server has no TLS, consider [SFTP](sftp.md), which always encrypts.

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [SFTP](sftp.md) · [TRANSFER](../../file-operations/transfer.md)
