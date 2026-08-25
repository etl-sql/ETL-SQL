# ODBC

Universal bridge for any source with a local ODBC driver. Supports both DSN-based and DSN-less
connections. SQL pushdown depends on the underlying provider. Use for databases and warehouses without
a native ETL-SQL connector.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `DSN` | Pre-configured Data Source Name | No |
| `DRIVER` | ODBC driver name in curly braces (e.g. `{SQLite3 ODBC Driver}`) | No |
| `SERVER` | Server name or IP | No |
| `PORT` | Listening port | No |
| `DATABASE` | Database name or file path | No |
| `UID` | Login username | No |
| `PASSWORD` | Login password | No |
| `CONNECT_TIMEOUT` | Login timeout in seconds | No |

> [!NOTE]
> For DSN-less connections you must provide `DRIVER` and at least one identifying property (`SERVER`
> or `DATABASE`).

## Authentication

ODBC authentication depends on the underlying driver and DSN:
- **DSN Connection**: References a system or user DSN with configured credentials or prompt.
- **Driver-Native Pass-Through**: Supply connection string keywords including `UID`, `PWD`, or `Trusted_Connection`.

## Examples

```sql
-- DSN pattern
CREATE CONNECTION odbc_prod AS ODBC(DSN='ProdSales', UID='etl', PASSWORD='pwd');

-- DSN-less SQLite
CREATE CONNECTION my_sqlite AS ODBC(DRIVER='{SQLite3 ODBC Driver}', DATABASE='C:\Data\local.db');
```

## Data warehouses via ODBC

The following platforms are supported through the ODBC bridge. Each requires the vendor's ODBC driver
installed on the machine running ETL-SQL.

| Platform | Native connector available? | Recommended ODBC driver |
| :--- | :---: | :--- |
| Amazon Redshift | No — use ODBC | Amazon Redshift ODBC Driver 2.x |
| Azure Synapse Analytics | No — use ODBC | ODBC Driver 18 for SQL Server |
| Databricks | No — use ODBC | Simba/Databricks ODBC Driver |
| Trino / Starburst | No — use ODBC | Starburst ODBC Driver |
| Dremio | No — use ODBC | Dremio ODBC Driver |
| Snowflake | **Yes** — use [`SNOWFLAKE`](snowflake.md) | n/a |
| BigQuery | **Yes** — use [`BIGQUERY`](bigquery.md) | n/a |

```sql
-- Amazon Redshift (DSN-less)
CREATE CONNECTION redshift AS ODBC(DRIVER='{Amazon Redshift ODBC Driver (x64)}',
         SERVER='mycluster.abc123.us-east-1.redshift.amazonaws.com',
         PORT='5439', DATABASE='analytics',
         UID='etl_user', PASSWORD='${REDSHIFT_PASSWORD}',
         TIMEOUT_SECONDS='1800');

-- Azure Synapse Analytics (uses SQL Server ODBC driver)
CREATE CONNECTION synapse AS ODBC(DRIVER='{ODBC Driver 18 for SQL Server}',
         SERVER='myworkspace.sql.azuresynapse.net',
         DATABASE='AnalyticsDB',
         UID='sqladmin', PASSWORD='${SYNAPSE_PASSWORD}',
         TIMEOUT_SECONDS='1800');

-- Databricks (personal access token auth)
CREATE CONNECTION databricks AS ODBC(DRIVER='{Simba Spark ODBC Driver}',
         SERVER='adb-1234567890.1.azuredatabricks.net',
         PORT='443',
         HTTPPath='/sql/1.0/warehouses/abcdef123456',
         AuthMech='3', UID='token', PASSWORD='${DATABRICKS_PAT}',
         SSL='1', TIMEOUT_SECONDS='1800');

-- Trino / Starburst
CREATE CONNECTION trino AS ODBC(DRIVER='{Starburst ODBC Driver}',
         SERVER='trino.internal.example.com', PORT='8443',
         DATABASE='analytics', UID='etl', PASSWORD='${TRINO_PASSWORD}',
         TIMEOUT_SECONDS='1800');

-- Dremio
CREATE CONNECTION dremio AS ODBC(DRIVER='{Dremio ODBC Driver 64-bit}',
         SERVER='dremio.internal.example.com', PORT='31010',
         UID='etl', PASSWORD='${DREMIO_PASSWORD}',
         TIMEOUT_SECONDS='1800');
```

> [!TIP]
> All ODBC data-warehouse connections benefit from `TIMEOUT_SECONDS = 1800` (30 minutes) since
> analytical queries routinely take minutes. Without it, the default 30-second command timeout causes
> premature cancellations on large scans.

## Troubleshooting

- **Driver Not Found**: Ensure 64-bit ODBC driver matching the ETL-SQL runtime architecture is installed.
- **Architecture Mismatch**: 32-bit drivers cannot be loaded by a 64-bit .NET runtime.
- **Timeout**: Set `TIMEOUT_SECONDS` in connection options to override driver default.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
- [Snowflake](snowflake.md) · [BigQuery](bigquery.md)
