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
| `TIMEOUT_SECONDS` | Command/query execution timeout in seconds (default: `30`) | No |
| `TABLE` | Default table context | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `CONNECTION_IDLE_LIFETIME` | Seconds before an idle connection is pruned | No |
| `SSL_MODE` | `Disable`, `Prefer`, `Require`, `VerifyCA`, `VerifyFull` | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass certificate validation (`TRUE`/`FALSE`) | No |

> [!NOTE]
> Use either structured options (`HOST`, `DATABASE`, `USER`, `PASSWORD`, ...) or a positional
> provider connection string, not both.

## Authentication

PostgreSQL supports standard credentials and SSL authentication:
- **Standard Authentication**: Set `USER` and `PASSWORD` options with `HOST` and `DATABASE` (supports `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'` with `Security:AllowedEnvVars`).
- **SSL / TLS**: Set `SSL_MODE` (`Disable`, `Prefer`, `Require`, `VerifyCA`, `VerifyFull`) and optional `TRUST_SERVER_CERTIFICATE`.

## Examples

```sql
-- Structured options using an allow-listed environment variable (Security:AllowedEnvVars)
CREATE CONNECTION pg_db AS POSTGRES(HOST='10.0.0.5', PORT=5432, DATABASE='inventory', USER='admin', PASSWORD='${PG_PASSWORD}');

-- Traditional string
CREATE CONNECTION pg_legacy AS POSTGRES('Host=localhost;Database=mydb;Username=etl;Password=pass');
```

## PostgreSQL SQL dialect notes

| Feature | Notes |
| :--- | :--- |
| Row capping | Uses `LIMIT` and `OFFSET` |
| `DUAL` | Not required — `SELECT 1;` runs without a `FROM` clause |
| Identifiers | Double-quoted (`"schema"."table"`); unquoted identifiers fold to lowercase. The connector leaves plain alphanumeric/underscore names unquoted and double-quotes names with special characters or explicit quotes |
| `ILIKE` / `SIMILAR` | Case-insensitive `LIKE` and SQL regex matching |
| `ON CONFLICT` / `RETURNING` | Native upsert (`DO NOTHING`, `DO UPDATE`) and DML row return clauses |
| `LATERAL` / `TABLESAMPLE` / `FILTER` | Lateral joins, table sampling, and aggregate `FILTER` / `WITHIN GROUP` clauses |
| `NOW()` / `DATE_TRUNC` / `DATE_PART` / `AGE` / `EXTRACT` | Native date/time functions |
| `STRING_AGG` / `ARRAY_AGG` / `ARRAY_TO_STRING` / `GENERATE_SERIES` | Aggregation, array, and set-returning functions |
| `JSON_BUILD_OBJECT` / `JSON_BUILD_ARRAY` / `JSONB_SET` / `JSON_ARRAY_ELEMENTS` | JSON and JSONB construction and extraction |
| `NULLS FIRST` / `NULLS LAST` | Supported in `ORDER BY` pushdown |

The linter's `DialectKeyword` rule warns on `TOP`, `PERCENT`, `ISNULL`, `CONVERT`, `ROWNUM`, `GETDATE`, and `SYSDATE` in PostgreSQL pushdown queries. Use `LIMIT`, `COALESCE`, `CAST`, and `NOW()` instead.

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
