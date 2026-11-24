# MSSQL

Connects to Microsoft SQL Server or Azure SQL Database. Supports full SQL pushdown, transactions,
stored procedure execution, and all SQL Server data types.

Aliases: `SQL`, `SQLSERVER`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `SERVER` | Server name or IP address | Yes (structured) |
| `PORT` | Listening port (default: `1433`) | No |
| `DATABASE` | Target database name | Yes (structured) |
| `AUTH_MODE` | Authentication mode: `SQL` (default), `WINDOWS`, `ENTRA_DEFAULT`, `ENTRA_MANAGED_IDENTITY`, `ENTRA_SERVICE_PRINCIPAL` | No |
| `USER` | SQL authentication username (`AUTH_MODE=SQL`) | No |
| `PASSWORD` | SQL authentication password (`AUTH_MODE=SQL`) or Entra service principal client secret (`AUTH_MODE=ENTRA_SERVICE_PRINCIPAL`) | No |
| `CLIENT_ID` | Optional user-assigned managed identity client ID (`ENTRA_MANAGED_IDENTITY`) or required app registration client ID (`ENTRA_SERVICE_PRINCIPAL`) | No |
| `TENANT_ID` | Microsoft Entra tenant ID (required for `ENTRA_SERVICE_PRINCIPAL`) | No |
| `TRUSTED_CONNECTION` | Use Windows Integrated Security (`TRUE`/`FALSE`); equivalent to `AUTH_MODE=WINDOWS` | No |
| `ENCRYPT` | Enable TLS encryption for the connection (`TRUE`/`FALSE`) | No |
| `USE_SSL` | Enable SSL/TLS encryption for the connection (`TRUE`/`FALSE`) | No |
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
> Authentication modes and options are mutually exclusive and validated at `CREATE CONNECTION`:
> do not combine `TRUSTED_CONNECTION` with Entra modes, `USER`/`PASSWORD` with `WINDOWS` or
> `ENTRA_DEFAULT`/`ENTRA_MANAGED_IDENTITY`, or `CLIENT_ID`/`TENANT_ID` with `SQL`/`WINDOWS`.

## Authentication

SQL Server and Azure SQL Database support five mutually exclusive authentication modes via `AUTH_MODE` (`PASSWORD`, `CLIENT_ID`, and `TENANT_ID` all accept `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'` with `Security:AllowedEnvVars`):

- **`SQL` (default)**: Set `USER` and `PASSWORD` (omit `TRUSTED_CONNECTION`, `CLIENT_ID`, `TENANT_ID`).
- **`WINDOWS` (Integrated Security)**: Set `AUTH_MODE='WINDOWS'` or `TRUSTED_CONNECTION=TRUE` and omit `USER`, `PASSWORD`, `CLIENT_ID`, and `TENANT_ID`.
- **`ENTRA_DEFAULT`**: Maps to SqlClient `Authentication=Active Directory Default` (`DefaultAzureCredential` via `Microsoft.Data.SqlClient.Extensions.Azure`). Omit `USER`, `PASSWORD`, `TRUSTED_CONNECTION`, `CLIENT_ID`, and `TENANT_ID`.
- **`ENTRA_MANAGED_IDENTITY`**: Maps to SqlClient `Authentication=Active Directory Managed Identity`. Omit `CLIENT_ID` for a system-assigned identity, or set `CLIENT_ID` for a user-assigned managed identity. Omit `USER`, `PASSWORD`, `TRUSTED_CONNECTION`, and `TENANT_ID`.
- **`ENTRA_SERVICE_PRINCIPAL`**: Maps to SqlClient `Authentication=Active Directory Service Principal`. Requires `CLIENT_ID`, `PASSWORD` (the client secret), and `TENANT_ID`. Omit `USER` and `TRUSTED_CONNECTION`.

## Examples

```sql
-- Standard SQL authentication using a secret reference (or '${MSSQL_PASSWORD}' / 'ENC:...')
CREATE CONNECTION m_sales AS MSSQL(SERVER='sql01', DATABASE='SalesDB', USER='etl_worker', PASSWORD='SECRET:prod/mssql/etl_worker');

-- Windows Integrated Security (traditional string)
CREATE CONNECTION m_hr AS MSSQL('Server=sql01;Database=HR;Trusted_Connection=True;');

-- Azure SQL with Entra Default credential chain
CREATE CONNECTION m_az_default AS MSSQL(SERVER='myserver.database.windows.net', DATABASE='Analytics', AUTH_MODE='ENTRA_DEFAULT', ENCRYPT=TRUE);

-- Azure SQL with user-assigned Managed Identity
CREATE CONNECTION m_az_mi AS MSSQL(SERVER='myserver.database.windows.net', DATABASE='Analytics', AUTH_MODE='ENTRA_MANAGED_IDENTITY', CLIENT_ID='SECRET:prod/azure/uami_client_id', ENCRYPT=TRUE);

-- Azure SQL with Entra Service Principal
CREATE CONNECTION m_az_sp AS MSSQL(SERVER='myserver.database.windows.net', DATABASE='Analytics', AUTH_MODE='ENTRA_SERVICE_PRINCIPAL', CLIENT_ID='SECRET:prod/azure/sp_client_id', PASSWORD='SECRET:prod/azure/sp_client_secret', TENANT_ID='SECRET:prod/azure/tenant_id', ENCRYPT=TRUE);

-- Read-only replica with SSL
CREATE CONNECTION m_ro AS MSSQL(SERVER='sql01', DATABASE='DW', TRUSTED_CONNECTION=TRUE,
         APPLICATION_INTENT=READONLY, ENCRYPT=TRUE, TRUST_SERVER_CERTIFICATE=TRUE);

SELECT order_id, customer, amount INTO #orders FROM m_sales.dbo.Orders WHERE order_date >= @start;
EXECUTE m_sales.dbo.UpdateSummary;

BEGIN TRANSACTION;
  INSERT INTO m_sales.dbo.Staging SELECT * FROM #processed;
COMMIT;
```

## SQL Server SQL dialect notes

| Feature | Notes |
| :--- | :--- |
| Row capping | Uses `TOP (n)` (with optional `PERCENT` / `WITH TIES`) or `ORDER BY ... OFFSET n ROWS FETCH NEXT m ROWS ONLY` |
| `DUAL` | Not used — omit `FROM DUAL` in native T-SQL (e.g., `SELECT GETDATE();`) |
| Identifiers | Square-bracket quoted (`[schema].[table]`); closing brackets inside names are escaped as `]]` |
| `NOLOCK` / `READUNCOMMITTED` / `ROWLOCK` / `TABLOCK` / `UPDLOCK` | Table locking hints |
| `CROSS APPLY` / `OUTER APPLY` / `TABLESAMPLE` | Row-wise lateral evaluation and sampling |
| `OUTPUT` / `INSERTED` / `DELETED` | DML row capture clauses |
| `OPTION` / `RECOMPILE` / `MAXDOP` | Query execution hints |
| `GETDATE` / `SYSDATETIME` / `DATEADD` / `DATENAME` / `EOMONTH` / `DATEFROMPARTS` | Native T-SQL date/time functions |
| `ISNULL` / `CONVERT` / `CHARINDEX` / `LEN` / `HASHBYTES` | Null handling, conversion, string, and hashing functions |

The linter's `DialectKeyword` rule warns on `LIMIT` and `ROWNUM` in SQL Server pushdown queries. Use `TOP` or `OFFSET ... FETCH NEXT` instead.

## Write Path & Transactions

- **Bulk / write path** — Cross-source `INSERT INTO` and `COPY INTO` write through `SqlBulkCopy` (streaming each batch via `WriteToServerAsync`). Same-connection `INSERT INTO ... SELECT` pushes down as native T-SQL. `COPY INTO ... APPEND=FALSE` runs `TRUNCATE TABLE` before loading.
- **`MERGE` behavior** — **Native SQL pushdown** when both source and target live on the same `MSSQL` connection. Cross-connection `MERGE` (for example from a `#temp` table) runs in the engine's bounded in-memory merge and rewrites the target via `TRUNCATE TABLE` + `SqlBulkCopy`. For large cross-source upserts, bulk-load into a staging table on the same `MSSQL` connection first, then run same-connection `MERGE`.
- **Transactions** — Full `ITransactionalDataSource` support. `BEGIN TRANSACTION` enlists `SqlBulkCopy`, `INSERT`/`UPDATE`/`DELETE`, `MERGE`, `TRUNCATE`, and `EXECUTE ... BEGIN ... END` on the same `SqlTransaction`; `COMMIT` persists and `ROLLBACK` discards.
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` and `TruncateAsync` return without touching SQL Server. Pushed-down `INSERT`, `UPDATE`, `DELETE`, `MERGE`, and `EXECUTE` log the compiled T-SQL without executing it.
- **Idempotent-load pattern** — Bulk-load `#delta` into a staging table (`INSERT INTO m_sales.dbo.Stage_Orders SELECT * FROM #delta`), then run a same-connection `MERGE` (or `EXECUTE m_sales BEGIN MERGE ... END`) inside `BEGIN TRANSACTION ... COMMIT`.

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
