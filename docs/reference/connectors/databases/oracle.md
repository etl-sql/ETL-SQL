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
| `TIMEOUT_SECONDS` | Command/query execution timeout in seconds (default: `30`) | No |
| `TABLE` | Default table context (e.g. `SCHEMA.TABLE`) | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum connections in the pool | No |
| `MAX_POOL_SIZE` | Maximum connections in the pool | No |
| `CONNECTION_LIFETIME` | Seconds a connection stays alive in the pool | No |

> [!CAUTION]
> `TNS_NAME` and `SERVICE_NAME` are **mutually exclusive**. Using both in the same connection raises a
> parse error.

## Authentication

Oracle supports Easy Connect and TNS authentication:
- **Easy Connect**: Set `USER` and `PASSWORD` with `HOST`, `PORT`, and `SERVICE_NAME` (supports `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'` with `Security:AllowedEnvVars`).
- **TNS Alias**: Set `TNS_NAME`, `USER`, and `PASSWORD` (or pass a raw connection string in the positional form).

## Examples

```sql
-- Service Name pattern using a secret reference (or '${ORACLE_PASSWORD}' / 'ENC:...')
CREATE CONNECTION o_dev AS ORACLE(HOST='oradb.local', PORT=1521, SERVICE_NAME='ORCL', USER='app_user', PASSWORD='SECRET:prod/oracle/app_user');

-- TNS Name pattern (traditional)
CREATE CONNECTION o_prod AS ORACLE('Data Source=MyTNS;User Id=app_user;Password=pwd;');
```

## Oracle SQL dialect notes

| Feature | Notes |
| :--- | :--- |
| Row capping | Uses `FETCH FIRST n ROWS ONLY` (Oracle 12c+) or `ROWNUM` |
| `DUAL` | Required in native Oracle SQL for queries without a table (e.g., `SELECT SYSDATE FROM DUAL` inside `EXECUTE ... BEGIN ... END`) |
| Identifiers | Double-quoted (`"SCHEMA"."TABLE"`); unquoted identifiers are uppercased by the connector to match Oracle's default uppercase storage, while already double-quoted names preserve case |
| `ROWNUM` / `ROWID` / `SAMPLE` | Row numbering, physical row addresses, and table sampling |
| `CONNECT BY` / `START WITH` / `PRIOR` / `NOCYCLE` / `LEVEL` / `SIBLINGS` | Hierarchical queries |
| `NVL` / `NVL2` / `DECODE` | Null-substitution and conditional expressions |
| `SYSDATE` / `TO_CHAR` / `TO_DATE` / `TO_NUMBER` / `TO_TIMESTAMP` | Date and type conversion functions |
| `MONTHS_BETWEEN` / `ADD_MONTHS` / `LAST_DAY` / `TRUNC` | Date arithmetic and truncation |
| `INSTR` / `SUBSTR` / `LPAD` / `RPAD` / `SYS_GUID` | String and identifier utility functions |
| `NULLS FIRST` / `NULLS LAST` | Supported in `ORDER BY` pushdown; table aliases omit `AS` |

The linter's `DialectKeyword` rule warns on `TOP`, `LIMIT`, and `ISNULL` in Oracle pushdown queries. Use `FETCH FIRST` (or `ROWNUM`) and `NVL` / `COALESCE` instead.

## Troubleshooting

- **ORA-12154 (TNS could not resolve service name)**: Verify `SERVICE_NAME` or EZConnect string.
- **ORA-01017 (invalid username/password)**: Confirm case-sensitivity of credentials.
- **Character Encoding**: Ensure UTF-8 or national charset compatibility on NVARCHAR columns.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [SQL Server](mssql.md) · [ODBC](odbc.md)
