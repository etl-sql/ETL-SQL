# FTP
Connects to an FTP or FTPS server for file transfer operations (SEND FILE, RECEIVE FILE). Not used for SELECT/INSERT — pair it with the TRANSFER operations.

Syntax:
  CREATE CONNECTION <name> AS FTP(
    HOST    = 'ftp.example.com',
    PORT    = 21,
    USER    = 'username',
    PASSWORD = '<password>',
    USE_SSL  = ON | OFF
  );

Options:
- **HOST** — FTP server hostname or IP (required)
- **PORT** — port number (default 21; FTPS typically 990)
- **USER** — login username (required)
- **PASSWORD** — login password (required)
- **USE_SSL** — use FTPS (FTP over TLS) (default OFF)
- **PASSIVE** — use passive mode (default ON)

```sql
CREATE CONNECTION DropzoneFTP AS FTP(
  HOST     = 'ftp.supplier.com',
  PORT     = 21,
  USER     = @ftp_user,
  PASSWORD = @ftp_password,
  USE_SSL  = ON
);

-- Download latest data file
RECEIVE FILE 'incoming/orders_latest.csv' TO 'C:\data\orders.csv' AT DropzoneFTP;

-- Upload processed result
SEND FILE 'C:\data\report.csv' TO 'outgoing/report.csv' AT DropzoneFTP;
```

References:
- [Data Connectors](../../../administration/platform/README.md)
