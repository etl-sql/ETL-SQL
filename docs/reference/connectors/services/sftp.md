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
| `HOST_KEY_FINGERPRINT` | Pinned server host-key fingerprint (`SHA256:base64` or MD5 hex). Required unless `ALLOW_UNPINNED_HOST_KEY` is set: an unpinned **or** mismatched host key **rejects** the connection (MITM protection). | Yes (unless opted out) |
| `ALLOW_UNPINNED_HOST_KEY` | `true`/`false` (default: `false`). Connect without a pinned host key, trusting whatever server answers. Logs a warning on every connection. **Not recommended** — see below. | No |
| `ATOMIC_UPLOAD` | `ON`/`OFF` (default: `OFF`). Upload to a unique sibling stage and publish through the server POSIX rename extension. Existing targets are never deleted first; unsupported servers fail without changing the target. | No |

> [!CAUTION]
> `PASSWORD` and `KEYFILE` are mutually exclusive. Providing both causes an authentication error.

> [!IMPORTANT]
> **Host-key verification is closed by default.** A connection is trusted only when the server's key
> matches `HOST_KEY_FINGERPRINT`. Get the value with `ssh-keygen -lf <server_host_key>` (the
> `SHA256:...` string it prints). Where the vendor grants rename permission, also set
> `ATOMIC_UPLOAD=TRUE` so a polling consumer never picks up a half-written file.
>
> If neither `HOST_KEY_FINGERPRINT` nor `ALLOW_UNPINNED_HOST_KEY` is set, the connection is **rejected**
> — there is no trust anchor, so the client cannot tell the real server from an interceptor.
> `ALLOW_UNPINNED_HOST_KEY = 'TRUE'` restores the permissive behaviour for trusted networks or
> migration, but it accepts any host key and leaves the transfer open to man-in-the-middle
> interception. Prefer pinning; treat the opt-out as temporary.

> [!NOTE]
> `ATOMIC_UPLOAD=ON` requires directory list/write/delete/rename permission for full stale-stage
> reconciliation and the SFTP server's POSIX rename extension for atomic replacement. A write-only
> account may still publish but cannot reconcile crash residue. The connector fails publication when
> POSIX rename is unsupported; it does not downgrade to delete-then-rename.

> [!NOTE]
> **Changed in v0.17.0.** Previously an unpinned connection proceeded with only a warning. It is now
> rejected unless `ALLOW_UNPINNED_HOST_KEY` is set, so an unverified transfer is a deliberate choice
> rather than the default. Existing scripts without `HOST_KEY_FINGERPRINT` will fail until you add
> either the pin (preferred) or the opt-out. A pin that is set but does not match is still always
> rejected — the opt-out does not weaken that.

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

-- Explicitly accepting an unverified host key (discouraged; prefer pinning above)
CREATE CONNECTION legacy_box AS SFTP(
  HOST                    = 'sftp.internal.lan',
  USER                    = 'etl',
  PASSWORD                = 's3cr3t',
  ALLOW_UNPINNED_HOST_KEY = 'TRUE'
);
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [FTP](ftp.md) · [TRANSFER](../../file-operations/transfer.md)
- [Transactional File Writes](../files/transactional-writes.md)
