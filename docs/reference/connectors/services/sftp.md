# SFTP

Secure File Transfer Protocol over SSH. Supports password and key-pair authentication (mutually
exclusive). Use with `SEND FILE` / `RECEIVE FILE` and remote file operations.

Aliases: `SSH`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server domain or IP address | Yes (structured) |
| `PORT` | Listening port (default: `22`) | No |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password — use for password auth only | No |
| `KEYFILE` | Path to the private SSH key — use for key auth only | No |
| `PASSPHRASE` | Passphrase for the private key (if set) | No |
| `TIMEOUT_SECONDS` | Connection timeout in seconds (default: `30`) | No |
| `HOST_KEY_FINGERPRINT` | Pinned server host-key fingerprint (`SHA256:base64` or MD5 hex). When set, a mismatch **rejects** the connection (MITM protection). Unset = connect but warn. | No |
| `ATOMIC_UPLOAD` | `true`/`false` (default: `false`). Upload to a temp name then rename into place so consumers never read a partial file. Requires rename permission on the target directory. | No |

> [!CAUTION]
> `PASSWORD` and `KEYFILE` are mutually exclusive. Providing both causes an authentication error.

> [!IMPORTANT]
> For internet-facing / vendor transfers, **pin `HOST_KEY_FINGERPRINT`**. Without it the client trusts
> whatever server answers, leaving outbound transfers open to man-in-the-middle interception; the
> connector logs a warning on every unpinned connection. Get the value with
> `ssh-keygen -lf <server_host_key>` (the `SHA256:...` string it prints). Where the vendor grants rename
> permission, also set `ATOMIC_UPLOAD=TRUE` so a polling consumer never picks up a half-written file.

## Examples

```sql
-- Password authentication
CREATE CONNECTION sftp_pwd AS SFTP(HOST='sftp.example.com', USER='admin', PASSWORD='s3cr3t');

-- Key-pair authentication (recommended for production)
CREATE CONNECTION sftp_key AS SFTP('sftp.example.com', USER='deploy', KEYFILE='/home/etl/.ssh/id_rsa', PASSPHRASE='keypass');

-- Hardened outbound vendor delivery: pinned host key + atomic upload
CREATE CONNECTION vendor_out AS SFTP(
  HOST                 = 'sftp.partner.com',
  USER                 = 'deploy',
  KEYFILE              = '/home/etl/.ssh/partner_rsa',
  HOST_KEY_FINGERPRINT = 'SHA256:n0uukFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc',
  ATOMIC_UPLOAD        = 'TRUE'
);
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [FTP](ftp.md) · [TRANSFER](../../file-operations/transfer.md)
