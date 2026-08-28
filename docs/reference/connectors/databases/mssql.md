# MSSQL

Connects to Microsoft SQL Server or Azure SQL Database. Supports full SQL pushdown, transactions,
stored procedure execution, and all SQL Server data types.

Aliases: `SQL`, `SQLSERVER`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `SERVER` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` | SQL authentication username | No |
| `PASSWORD` | SQL authentication password | No |
| `TRUSTED_CONNECTION` | Use Windows Integrated Security (`TRUE`/`FALSE`) | No |
| `USE_SSL` | Enable SSL encryption for the connection (`TRUE`/`FALSE`) | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass SSL certificate validation (`TRUE`/`FALSE`) | No |
| `APPLICATION_INTENT` | `READWRITE` or `READONLY` (for AG replicas) | No |
| `MULTI_SUBNET_FAILOVER` | Optimize failover for multi-subnet clusters (`TRUE`/`FALSE`) | No |
| `CONNECT_TIMEOUT` | Seconds to wait for a connection (default: `15`) | No |
| `TIMEOUT_SECONDS` | Command/query execution timeout in seconds (default: `30`) | No |
| `POOLING` | Enable provider connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum connections kept in the pool | No |
| `MAX_POOL_SIZE` | Maximum connections allowed in the pool | No |
| `POOL_LIFETIME` | Seconds before a pooled connection is recycled | No |
| `TABLE` | Default table context (e.g. `dbo.Employees`) | No |

> [!NOTE]
> Do not set `USER`/`PASSWORD` when using `TRUSTED_CONNECTION=TRUE`. They are mutually exclusive
> authentication methods.

## Authentication

SQL Server supports two mutually exclusive authentication methods:
- **SQL Authentication**: Set `USER` and `PASSWORD`.
- **Integrated Security (Windows Authentication)**: Set `TRUSTED_CONNECTION=TRUE` and omit `USER`/`PASSWORD`.

## Examples

```sql
-- Standard SQL authentication
CREATE CONNECTION m_sales AS MSSQL(SERVER='sql01', DATABASE='SalesDB', USER='etl_worker', PASSWORD='s3cr3t');

-- Windows Integrated Security (traditional string)
CREATE CONNECTION m_hr AS MSSQL('Server=sql01;Database=HR;Trusted_Connection=True;');

-- Read-only replica with SSL
CREATE CONNECTION m_ro AS MSSQL(SERVER='sql01', DATABASE='DW', TRUSTED_CONNECTION=TRUE,
         APPLICATION_INTENT=READONLY, USE_SSL=TRUE, TRUST_SERVER_CERTIFICATE=TRUE);

SELECT order_id, customer, amount INTO #orders FROM m_sales.dbo.Orders WHERE order_date >= @start;
EXECUTE m_sales.dbo.UpdateSummary;

BEGIN TRANSACTION;
  INSERT INTO m_sales.dbo.Staging SELECT * FROM #processed;
COMMIT;
```

## Troubleshooting

- **Login Failed for User**: Verify credentials, database existence, and whether SQL Server is configured for mixed-mode authentication.
- **SSL / Certificate Error**: For self-signed certificates in non-production, set `TRUST_SERVER_CERTIFICATE=TRUE`.
- **Connection Timeout**: Verify SQL Server is listening on TCP/IP and SQL Browser is active for named instances.

## Verified Gateway viewer context

An approved SQL Server Gateway resource can opt into signed application viewer context. SQL Server
still authenticates the configured service credential. The Gateway requires `ORIGINAL_LOGIN()` to
match the resource's `executing-credential-id` and installs viewer values through parameterized
`sys.sp_set_session_context` calls. Viewer claims never select SQL Server logins, users, or roles.

Every installed key is cleared before the connection returns to the pool. Cancellation, command
timeout, provider failure, and killed-session paths roll back and clear or evict the affected pool.
Database policies consuming `SESSION_CONTEXT(N'etlsql.viewer_id')` must deny missing values.

See [Verified Viewer Context](../../../architecture/decisions/verified-viewer-context.md) for the
assurance boundary, allowed claims, configuration, and certification evidence.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [Verified Viewer Context](../../../architecture/decisions/verified-viewer-context.md)
- [PostgreSQL](postgres.md) · [Oracle](oracle.md) · [ODBC](odbc.md)
