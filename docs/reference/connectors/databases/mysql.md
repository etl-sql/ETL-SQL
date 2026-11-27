# MYSQL

Native connector for MySQL and MariaDB databases using the MySqlConnector driver. Supports full SQL
pushdown, schema introspection, high-throughput bulk inserts via `MySqlBulkCopy`, transactions,
connection pooling, and SSL.

Aliases: `MARIADB`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` / `SERVER` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` / `UID` | Login username | Yes (structured) |
| `PASSWORD` | Login password | Yes (structured) |
| `PORT` | Listening port (default: `3306`) | No |
| `SSL_MODE` | TLS mode: `None`, `Preferred`, `Required`, `VerifyCA`, `VerifyFull` (default: `Preferred`) | No |
| `ALLOW_PUBLIC_KEY_RETRIEVAL` | Allow RSA public key retrieval from server (`TRUE`/`FALSE`, default: `FALSE`) | No |
| `ALLOW_USER_VARIABLES` | Allow user-defined variables like `@var` inside queries (`TRUE`/`FALSE`, default: `FALSE`) | No |
| `TIMEOUT_SECONDS` | Command timeout in seconds (default: `30`) | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `CONNECTION_IDLE_TIMEOUT` | Seconds before idle pooled connections are removed | No |
| `CONNECTION_LIFETIME` | Maximum age in seconds for a pooled connection | No |
| `TABLE` | Default table context | No |

> [!NOTE]
> `HOST` and `SERVER` are synonyms (`HOST` takes precedence if both are set), and `USER` and `UID`
> are synonyms (`USER` takes precedence). Use structured options or a positional connection string,
> not both.

## Authentication

MySQL supports standard user authentication and SSL:
- **Standard Authentication**: Set `USER` (or `UID`) and `PASSWORD` options (supports `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'` with `Security:AllowedEnvVars`).
- **SSL / TLS**: Set `SSL_MODE` (`None`, `Preferred`, `Required`, `VerifyCA`, `VerifyFull`).

## Examples

```sql
-- Structured property connection using allow-listed environment variable (Security:AllowedEnvVars)
CREATE CONNECTION mysql_db AS MYSQL(HOST='127.0.0.1', PORT=3306, DATABASE='inventory', USER='etl_user', PASSWORD='${MYSQL_PASSWORD}', ALLOW_PUBLIC_KEY_RETRIEVAL=TRUE);

-- Traditional connection string
CREATE CONNECTION mysql_legacy AS MYSQL('Server=localhost;Database=mydb;Uid=etl;Pwd=pass;AllowUserVariables=True;');
```

## Supported MySQL-specific SQL

The connector supports native MySQL functions and constructs when pushing queries down to the remote
server.

| Feature | Notes |
| :--- | :--- |
| `LIMIT` / `OFFSET` | MySQL standard row capping |
| `ON DUPLICATE KEY UPDATE` | Upsert behavior |
| `IFNULL` / `COALESCE` | Null-substitution functions |
| `GROUP_CONCAT` | Group string concatenation |
| `JSON_OBJECT` / `JSON_ARRAY` / `JSON_EXTRACT` | Semi-structured data manipulation |
| `STR_TO_DATE` / `DATE_FORMAT` | Date string conversion and formatting |

The keywords `TOP`, `ROWNUM`, and `PERCENT` are excluded. The T-SQL 2-argument `ISNULL` function is
excluded (use MySQL's `IFNULL` or `COALESCE`).

## Write Path & Transactions

- **Bulk / write path** — Cross-source `INSERT INTO` and `COPY INTO` write through `MySqlBulkCopy` (`WriteToServerAsync`, using `TIMEOUT_SECONDS` as `BulkCopyTimeout`). Same-connection `INSERT INTO ... SELECT` pushes down as native MySQL SQL. `COPY INTO ... APPEND=FALSE` runs `TRUNCATE TABLE` first.
- **`MERGE` behavior** — Engine-emulated. `MERGE` against a `MYSQL` target evaluates in bounded engine memory and rewrites the target via `TRUNCATE TABLE` + `MySqlBulkCopy`. For large tables, load `#delta` into a staging table on the same MySQL connection and run native `INSERT INTO target ... SELECT ... FROM stage ON DUPLICATE KEY UPDATE ...` inside `EXECUTE mysql_db BEGIN ... END`.
- **Transactions** — Full `ITransactionalDataSource` support. `BEGIN TRANSACTION` enlists `MySqlBulkCopy`, pushed-down DML, and `EXECUTE ... BEGIN ... END` on the same `MySqlTransaction` (note that MySQL `TRUNCATE TABLE` causes an implicit commit on the server).
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` and `TruncateAsync` are no-ops. Pushed-down DML and `EXECUTE` blocks log the compiled SQL without executing it.
- **Idempotent-load pattern** — Load `#delta` into a staging table via `INSERT INTO mysql_db.stage_items SELECT * FROM #delta`, then run `INSERT ... ON DUPLICATE KEY UPDATE` inside `EXECUTE mysql_db BEGIN ... END` within a `BEGIN TRANSACTION ... COMMIT` block (using `DELETE FROM stage_items` rather than `TRUNCATE` if you need rollback across the whole unit).

## Troubleshooting

- **Access Denied**: Confirm user grants include remote host wildcard or explicit IP permissions (`'user'@'%' or 'user'@'host'`).
- **Packet Too Large**: For large batch inserts, increase `max_allowed_packet` on the MySQL server.
- **Unknown Database**: Verify `DATABASE` exists and user has `SELECT`/`INSERT` privileges.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [PostgreSQL](postgres.md) · [SQL Server](mssql.md)
