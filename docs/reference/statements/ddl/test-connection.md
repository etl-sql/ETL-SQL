# TEST CONNECTION
Actively diagnoses a catalog connection and prints a plain-English troubleshooting report. Layers are checked in order: DNS resolution, TCP reachability, TLS handshake, and connector-specific credential authentication. The report stops at the first failure with a specific remedy.

## Syntax
```sql
TEST CONNECTION <alias>;
TEST CONNECTION <alias> INTO #results;   -- capture the report as a table instead of printing
```

## Example
```sql
CREATE CONNECTION ProdDB AS MSSQL (
  SERVER   = 'sql-prod.corp,1433',
  DATABASE = 'Sales',
  ENCRYPT  = true,
  PASSWORD = 'SECRET:prod-db'
);

TEST CONNECTION ProdDB;
```
```
Connection diagnostic for 'ProdDB' (MSSQL):
  [ OK ] POLICY  Destination permitted by active security policy.
  [ OK ] DNS     'sql-prod.corp' resolved to 10.2.4.9.
  [ OK ] TCP     Port 1433 on sql-prod.corp is reachable.
  [ OK ] TLS     TLS handshake succeeded (Tls13); certificate 'CN=sql-prod.corp' valid until 2027-01-30.
  [ OK ] AUTH    MSSQL authentication succeeded.
Result: all attempted checks passed.
```

## Notes
- **Governed like a real connection.** Probing routes through the same egress controls as an actual connect: the destination connector type and host are re-authorized against the active security policy *before any packet is sent*, and each resolved address is re-checked at connect time to block DNS rebinding to internal ranges. A policy denial is reported as a `DENY` step and no probe runs.
- **Secrets are never echoed** in the report, including in error text.
- **Credential authentication is connector-specific.** MSSQL, Postgres, MySQL, and SFTP run an auth probe. Unsupported connectors report `AUTH` as skipped and should be verified with a connector-specific operation.
- **SFTP host keys are validated when pinned.** `HOST_KEY_FINGERPRINT` produces a `HOST_KEY` step before `AUTH`; a mismatch fails closed before credentials are trusted. When no fingerprint is pinned, the observed fingerprint is shown so an administrator can verify and pin it.
- File-based / local connectors (CSV, Parquet, SQLite) report the network layers as not applicable.
- The TCP layer needs a port: it is taken from the connector, then a `PORT` option, then a well-known default. If none is known, the TCP check is skipped with guidance to add `PORT`.
- The connect timeout is governed by `Engine:Diagnostics:ProbeTimeoutSeconds` (default 5).
- See: CREATE CONNECTION, `eng.connections`, ALTER CONNECTION

References:
- [Statements](../README.md)
