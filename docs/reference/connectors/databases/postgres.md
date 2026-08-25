# POSTGRES

Connects to PostgreSQL databases using the Npgsql driver. Supports full SQL pushdown, schema
introspection, PostgreSQL-specific types, transactions, connection pooling, and SSL.

Aliases: `NPSQL`, `PG`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `PORT` | Listening port (default: `5432`) | No |
| `TABLE` | Default table context | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `CONNECTION_IDLE_LIFETIME` | Seconds before an idle connection is pruned | No |
| `SSL_MODE` | `DISABLE`, `PREFER`, `REQUIRE`, `VERIFY_CA`, `VERIFY_FULL` | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass certificate validation (`TRUE`/`FALSE`) | No |

## Authentication

PostgreSQL supports standard credentials and SSL authentication:
- **Standard Authentication**: Set `USER` and `PASSWORD` options with `HOST` and `DATABASE`.
- **SSL / TLS**: Set `SSL_MODE` (`DISABLE`, `PREFER`, `REQUIRE`, `VERIFY_CA`, `VERIFY_FULL`) and optional `TRUST_SERVER_CERTIFICATE`.

## Examples

```sql
-- Structured
CREATE CONNECTION pg_db AS POSTGRES(HOST='10.0.0.5', PORT=5432, DATABASE='inventory', USER='admin', PASSWORD='s3cr3t');

-- Traditional string
CREATE CONNECTION pg_legacy AS POSTGRES('Host=localhost;Database=mydb;Username=etl;Password=pass');
```

## Verified Gateway viewer context

PostgreSQL is the first Gateway connector that can consume verified application viewer context.
PostgreSQL authenticates the Gateway-local service credential, not the viewer. The Portal signs the
viewer envelope, the Gateway verifies it, and the connector installs only allowlisted values with
parameterized transaction-local `set_config` calls.

- **Role safety** — OIDC roles and groups never cause `SET ROLE` and cannot be custom claim keys.
- **Lifetime** — Settings exist only in the transaction that runs the operation.
- **Pool cleanup** — Commit, rollback, or disposal clears settings before pool reuse.
- **Fail closed** — Missing, forged, expired, replayed, cross-boundary, or unlisted context denies
  the operation.
- **Database policy** — Read `current_setting('etlsql.viewer_id', true)` and deny null or empty
  values.

See [Verified Viewer Context](../../../architecture/decisions/verified-viewer-context.md) for the
assurance boundary, envelope, reserved keys, audit contract, and resource setup.

## Troubleshooting

- **Password Authentication Failed**: Verify credentials and `pg_hba.conf` rules on the PostgreSQL server.
- **SSL Connection Error**: For self-signed certificates in isolated test environments, set `TRUST_SERVER_CERTIFICATE=TRUE`.
- **Connection Refused**: Check PostgreSQL port (default 5432) and `listen_addresses`.

## References

- [Database Connectors](README.md)
- [Verified Viewer Context](../../../architecture/decisions/verified-viewer-context.md)
- [Connectors](../README.md)
- [SQL Server](mssql.md) · [MySQL & MariaDB](mysql.md)
