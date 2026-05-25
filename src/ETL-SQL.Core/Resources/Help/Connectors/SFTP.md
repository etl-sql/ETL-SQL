# SFTP
Connects to an SFTP (SSH File Transfer Protocol) server for secure file transfer. Use SEND FILE and RECEIVE FILE with this connection.

Syntax:
  CREATE CONNECTION <name> ON SFTP(
    HOST       = 'sftp.example.com',
    PORT       = 22,
    USER       = 'username',
    PASSWORD   = '<password>',
    KEYFILE    = 'path/to/key',
    PASSPHRASE = '<key passphrase>'
  );

Options:
  HOST        — SFTP server hostname or IP (required)
  PORT        — SSH port (default 22)
  USER        — SSH username (required)
  PASSWORD    — password authentication
  KEYFILE     — path to a private key file (PEM or PPK format)
  PASSPHRASE  — passphrase protecting the private key

```sql
CREATE CONNECTION PartnerSFTP ON SFTP(
  HOST    = 'sftp.partner.com',
  PORT    = 22,
  USER    = @sftp_user,
  KEYFILE = 'C:\keys\partner_rsa'
);

-- Download today's data file
RECEIVE FILE 'incoming/orders_today.csv' TO 'C:\data\orders.csv' AT PartnerSFTP;

-- Process, then upload the result
SEND FILE 'C:\data\summary.csv' TO 'outgoing/summary.csv' AT PartnerSFTP;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
