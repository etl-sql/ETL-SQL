# ORACLE

Connects to Oracle Database using either an **Easy Connect** host/service combination or a
pre-configured **TNS** alias. Supports full SQL pushdown, transactions, and connection pooling.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP | Yes (Service pattern) |
| `PORT` | Listening port (default: `1521`) | No |
| `SERVICE_NAME` | Oracle service name | Yes (Service pattern) |
| `TNS_NAME` | Oracle TNS alias | Yes (TNS pattern) |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `TABLE` | Default table context (e.g. `SCHEMA.TABLE`) | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum connections in the pool | No |
| `MAX_POOL_SIZE` | Maximum connections in the pool | No |
| `CONNECTION_LIFETIME` | Seconds a connection stays alive in the pool | No |

> [!CAUTION]
> `TNS_NAME` and `SERVICE_NAME` are **mutually exclusive**. Using both in the same connection raises a
> parse error.

## Examples

```sql
-- Service Name pattern (structured)
CREATE CONNECTION o_dev AS ORACLE(HOST='oradb.local', PORT=1521, SERVICE_NAME='ORCL', USER='app_user', PASSWORD='pwd');

-- TNS Name pattern (traditional)
CREATE CONNECTION o_prod AS ORACLE('Data Source=MyTNS;User Id=app_user;Password=pwd;');
```

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [SQL Server](mssql.md) · [ODBC](odbc.md)
