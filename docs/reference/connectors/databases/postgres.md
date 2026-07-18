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

## Examples

```sql
-- Structured
CREATE CONNECTION pg_db AS POSTGRES(HOST='10.0.0.5', PORT=5432, DATABASE='inventory', USER='admin', PASSWORD='s3cr3t');

-- Traditional string
CREATE CONNECTION pg_legacy AS POSTGRES('Host=localhost;Database=mydb;Username=etl;Password=pass');
```

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [SQL Server](mssql.md) · [MySQL & MariaDB](mysql.md)
