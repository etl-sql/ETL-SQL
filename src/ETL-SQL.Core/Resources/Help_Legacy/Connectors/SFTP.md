# SFTP
Connects to an SFTP (SSH File Transfer Protocol) server for secure file transfer. Use SEND FILE and RECEIVE FILE with this connection.

Syntax:
  CREATE CONNECTION <name> AS SFTP(
    HOST       = 'sftp.example.com',
    PORT       = 22,
    USER       = 'username',
    PASSWORD   = '<password>',
    KEYFILE    = 'path/to/key',
    PASSPHRASE = '<key passphrase>'
  );

Options:
- **HOST** — SFTP server hostname or IP (required)
- **PORT** — SSH port (default 22)
- **USER** — SSH username (required)
- **PASSWORD** — password authentication
- **KEYFILE** — path to a private key file (PEM or PPK format)
- **PASSPHRASE** — passphrase protecting the private key
- **TIMEOUT_SECONDS** — connection timeout in seconds (default 30)
- **HOST_KEY_FINGERPRINT** — pinned server host-key fingerprint (`SHA256:base64` or MD5 hex). When set, a mismatch **rejects** the connection, protecting outbound transfers against man-in-the-middle interception. When unset the connection proceeds (backward compatible) but logs a warning — **pin it for internet-facing / vendor transfers.** Get the value with `ssh-keygen -lf <server_host_key>`.
- **ATOMIC_UPLOAD** — `true`/`false` (default `false`). When `true`, `SEND FILE` uploads to a temporary name and renames into place on completion, so a polling consumer never reads a partially written file. Requires **rename** permission on the target directory — leave off for write-only vendor drop boxes.

Security note: for outbound vendor deliveries, pin `HOST_KEY_FINGERPRINT` and, where the vendor permits rename, enable `ATOMIC_UPLOAD`.

```sql
-- Hardened outbound vendor delivery: pinned host key + atomic upload
CREATE CONNECTION PartnerSFTP AS SFTP(
  HOST                 = 'sftp.partner.com',
  PORT                 = 22,
  USER                 = @sftp_user,
  KEYFILE              = 'C:\keys\partner_rsa',
  HOST_KEY_FINGERPRINT = 'SHA256:n0uukFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc',
  ATOMIC_UPLOAD        = 'TRUE'
);

-- Download today's data file
RECEIVE FILE 'incoming/orders_today.csv' TO 'C:\data\orders.csv' AT PartnerSFTP;

-- Process, then upload the result (written atomically: temp name, then rename)
SEND FILE 'C:\data\summary.csv' TO 'outgoing/summary.csv' AT PartnerSFTP;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
