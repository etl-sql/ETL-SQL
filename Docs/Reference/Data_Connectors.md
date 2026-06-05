# ETL-SQL Data Connectors: Reference & Guide

Connectors define how the ETL-SQL engine interacts with external data sources. This document provides complete option references and instructional examples for every supported connector type.

---

## 1. Connection Syntax

Every connector supports two equivalent syntaxes. Use whichever is easiest to read, version-control, or encrypt.

### 1.1 Traditional (String-based)
The connection string is the primary argument. Ideal for native driver DSNs or encrypted (`ENC:…`) secrets.
```sql
CREATE CONNECTION <name> AS <type>('<connection_string>');
```

### 1.2 Structured (Property-based)
All parameters are passed explicitly as named options. Recommended for readability and AI-assisted authoring.
```sql
CREATE CONNECTION <name> AS <type>(<property>=<value>, ...);
```

### 1.3 Mixed (String + extra options)
When the connector accepts a primary connection string plus additional settings:
```sql
CREATE CONNECTION <name> AS <type>('<connection_string>', <option>=<value>, ...);
```

> [!TIP]
> All forms produce identical results. Mix them on a per-connection basis; there is no performance difference.

### 1.3 Encrypted Connection Strings (`ENC:`)
Sensitive connection strings can be encrypted using the engine's master password. The engine detects the `ENC:` prefix automatically and decrypts the string before connecting.

```sql
-- Set the session master password first
USE PASSWORD = 'myMasterSecret';

-- The engine will decrypt this at connection time
CREATE CONNECTION secure_db AS MSSQL('ENC:U2FsdGVkX1+...');
```

> [!IMPORTANT]
> The `ENC:` prefix is handled entirely by the engine — connectors never see the encrypted string. Use the **ETL-SQL Encryptor** tool to encrypt strings using your master password.

### 1.4 Error Handling & Truncation Options
All database and flat-file connectors support options to control how the engine behaves when inserting data that exceeds column lengths or violates types:

| Option | Description | Values | Default |
| :--- | :--- | :--- | :--- |
| `TRUNCATE_STRING` | Controls string truncation behavior. When `ON`/`TRUE`, strings exceeding target column/file width are silently truncated to fit. When `OFF`/`FALSE`, truncation causes a validation failure. | `ON` / `OFF` / `TRUE` / `FALSE` | `OFF` |
| `SKIP_ERROR` | Controls error tolerance. When `ON`/`TRUE`, conversion errors set the column value to `NULL` and proceed. Primary key or unique constraint violations skip the entire row. When `OFF`/`FALSE`, any validation failure aborts the execution. | `ON` / `OFF` / `TRUE` / `FALSE` | `OFF` |

> [!NOTE]
> Connection-level settings override script-level global `SET` defaults.
> Database connections utilizing native high-performance bulk protocols (e.g. `SqlBulkCopy` / `COPY`) bypass engine-side validation to prioritize throughput and will fail fast natively if truncation or type boundaries are violated, regardless of these settings.

---

## 2. Relational Database Connectors

SQL-capable connectors support pushdown: ETL-SQL executes operations natively on the remote server whenever possible, avoiding unnecessary data movement.

### 2.1 Microsoft SQL Server (`MSSQL`)
Aliases: `SQL`, `SQLSERVER`

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
| `CONNECT_TIMEOUT` | Seconds to wait for a connection (Default: `15`) | No |
| `MIN_POOL_SIZE` | Minimum connections kept in the pool | No |
| `MAX_POOL_SIZE` | Maximum connections allowed in the pool | No |
| `POOL_LIFETIME` | Seconds before a pooled connection is recycled | No |
| `TABLE` | Default table context (e.g. `dbo.Employees`) | No |

> [!NOTE]
> Do not set `USER`/`PASSWORD` when using `TRUSTED_CONNECTION=TRUE`. They are mutually exclusive authentication methods.

*Examples:*
```sql
-- Standard SQL authentication
CREATE CONNECTION m_sales AS MSSQL(SERVER='sql01', DATABASE='SalesDB', USER='etl_worker', PASSWORD='s3cr3t');

-- Windows Integrated Security (traditional string)
CREATE CONNECTION m_hr AS MSSQL('Server=sql01;Database=HR;Trusted_Connection=True;');

-- Read-only replica with SSL
CREATE CONNECTION m_ro AS MSSQL(SERVER='sql01', DATABASE='DW', TRUSTED_CONNECTION=TRUE,
         APPLICATION_INTENT=READONLY, USE_SSL=TRUE, TRUST_SERVER_CERTIFICATE=TRUE);
```

---

### 2.2 PostgreSQL (`POSTGRES`)
Aliases: `NPSQL`, `PG`

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `PORT` | Listening port (Default: `5432`) | No |
| `TABLE` | Default table context | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `CONNECTION_IDLE_LIFETIME` | Seconds before an idle connection is pruned | No |
| `SSL_MODE` | `DISABLE`, `PREFER`, `REQUIRE`, `VERIFY_CA`, `VERIFY_FULL` | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass certificate validation (`TRUE`/`FALSE`) | No |

*Examples:*
```sql
-- Structured
CREATE CONNECTION pg_db AS POSTGRES(HOST='10.0.0.5', PORT=5432, DATABASE='inventory', USER='admin', PASSWORD='s3cr3t');

-- Traditional string
CREATE CONNECTION pg_legacy AS POSTGRES('Host=localhost;Database=mydb;Username=etl;Password=pass');
```

---

### 2.3 MySQL & MariaDB (`MYSQL`)
Aliases: `MARIADB`

Native connector for MySQL and MariaDB databases. Supports full SQL pushdown, schema introspection, high-throughput bulk inserts via `MySqlBulkCopy`, and transactions.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` / `SERVER` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` / `UID` | Login username | Yes (structured) |
| `PASSWORD` | Login password | Yes (structured) |
| `PORT` | Listening port (Default: `3306`) | No |
| `SSL_MODE` | TLS mode: `NONE`, `PREFERRED`, `REQUIRED`, `VERIFYCA`, `VERIFYFULL` (Default: `PREFERRED`) | No |
| `ALLOW_PUBLIC_KEY_RETRIEVAL` | Allow RSA public key retrieval from server (`TRUE`/`FALSE`, Default: `FALSE`) | No |
| `ALLOW_USER_VARIABLES` | Allow user-defined variables like `@var` inside queries (`TRUE`/`FALSE`, Default: `FALSE`) | No |
| `TIMEOUT_SECONDS` | Command timeout in seconds (Default: `30`) | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `TABLE` | Default table context | No |

*Examples:*
```sql
-- Structured property connection
CREATE CONNECTION mysql_db AS MYSQL(HOST='127.0.0.1', PORT=3306, DATABASE='inventory', USER='etl_user', PASSWORD='s3cr3t', ALLOW_PUBLIC_KEY_RETRIEVAL=TRUE);

-- Traditional connection string
CREATE CONNECTION mysql_legacy AS MYSQL('Server=localhost;Database=mydb;Uid=etl;Pwd=pass;AllowUserVariables=True;');
```

**Supported MySQL-specific SQL:**
The connector supports native MySQL functions and constructs when pushing queries down to the remote server.

| Feature | Notes |
| :--- | :--- |
| `LIMIT` / `OFFSET` | MySQL standard row capping |
| `ON DUPLICATE KEY UPDATE` | Upsert behavior |
| `IFNULL` / `COALESCE` | Null-substitution functions |
| `GROUP_CONCAT` | Group string concatenation |
| `JSON_OBJECT` / `JSON_ARRAY` / `JSON_EXTRACT` | Semi-structured data manipulation |
| `STR_TO_DATE` / `DATE_FORMAT` | Date string conversion and formatting |

The keywords `TOP`, `ROWNUM`, and `PERCENT` are excluded. The T-SQL 2-argument `ISNULL` function is excluded (use MySQL's `IFNULL` or `COALESCE`).

---

### 2.4 Oracle (`ORACLE`)
Oracle supports two patterns: **Service Name** (for direct connection) and **TNS** (for pre-configured aliases). They are mutually exclusive.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP | Yes (Service pattern) |
| `PORT` | Listening port (Default: `1521`) | No |
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
> `TNS_NAME` and `SERVICE_NAME` are **mutually exclusive**. Using both in the same connection will raise a parse error.

*Examples:*
```sql
-- Service Name pattern (structured)
CREATE CONNECTION o_dev AS ORACLE(HOST='oradb.local', PORT=1521, SERVICE_NAME='ORCL', USER='app_user', PASSWORD='pwd');

-- TNS Name pattern (traditional)
CREATE CONNECTION o_prod AS ORACLE('Data Source=MyTNS;User Id=app_user;Password=pwd;');
```

---

### 2.5 ODBC Bridge (`ODBC`)
Universal bridge for any source with a local ODBC driver. Supports both DSN-based and DSN-less connections. SQL pushdown depends on the underlying provider.

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
> For DSN-less connections you must provide `DRIVER` and at least one identifying property (`SERVER` or `DATABASE`).

*Examples:*
```sql
-- DSN pattern
CREATE CONNECTION odbc_prod AS ODBC(DSN='ProdSales', UID='etl', PASSWORD='pwd');

-- DSN-less SQLite
CREATE CONNECTION my_sqlite AS ODBC(DRIVER='{SQLite3 ODBC Driver}', DATABASE='C:\Data\local.db');
```

#### Data Warehouses via ODBC

The following platforms are supported through the ODBC bridge. Each requires the vendor's ODBC driver to be installed on the machine running ETL-SQL.

| Platform | Native connector available? | Recommended ODBC driver |
| :--- | :---: | :--- |
| Amazon Redshift | No — use ODBC | Amazon Redshift ODBC Driver 2.x |
| Azure Synapse Analytics | No — use ODBC | ODBC Driver 18 for SQL Server |
| Databricks | No — use ODBC | Simba/Databricks ODBC Driver |
| Trino / Starburst | No — use ODBC | Starburst ODBC Driver |
| Dremio | No — use ODBC | Dremio ODBC Driver |
| Snowflake | **Yes** — use `SNOWFLAKE` connector | n/a |
| BigQuery | **Yes** — use `BIGQUERY` connector | n/a |

*Connection string examples:*
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
> All ODBC data warehouse connections benefit from `TIMEOUT_SECONDS = 1800` (30 minutes) since analytical queries routinely take minutes. Without it, the default 30-second command timeout causes premature cancellations on large scans.

---

### 2.6 Snowflake (`SNOWFLAKE`)

Native connector for Snowflake Cloud Data Platform. Supports full SQL pushdown, schema introspection, batch reads/writes, and transactions. Two authentication modes are supported: username + password, and private-key JWT (recommended for production).

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Account identifier — plain (`myorg-myaccount`) or full FQDN (`myorg-myaccount.snowflakecomputing.com`). The `.snowflakecomputing.com` suffix is stripped automatically. | Yes |
| `USERNAME` | Snowflake login name | Yes |
| `PASSWORD` | Password (for username/password auth) | Cond. |
| `PRIVATE_KEY_FILE` | Path to RSA private key PEM file (enables JWT auth; takes precedence over `PASSWORD`) | Cond. |
| `WAREHOUSE` | Virtual warehouse for query execution (e.g. `COMPUTE_WH`) | No |
| `DATABASE` | Target database | No |
| `SCHEMA` | Target schema (defaults to `PUBLIC`) | No |

> [!NOTE]
> Either `PASSWORD` or `PRIVATE_KEY_FILE` must be supplied. If both are present, `PRIVATE_KEY_FILE` wins and `PASSWORD` is ignored.

> [!TIP]
> For production workloads, use key-pair authentication. Generate an RSA key pair, upload the public key to Snowflake (`ALTER USER … SET RSA_PUBLIC_KEY = '…'`), and store the private key path in `PRIVATE_KEY_FILE`.

*Examples:*
```sql
-- Username + password
CREATE CONNECTION sf AS SNOWFLAKE(HOST='myorg-myaccount', USERNAME='etl_user', PASSWORD='s3cr3t',
         DATABASE='PROD', SCHEMA='STAGING', WAREHOUSE='LOAD_WH');

-- Private-key JWT (recommended for production)
CREATE CONNECTION sf AS SNOWFLAKE(HOST='myorg-myaccount.snowflakecomputing.com',
         USERNAME='etl_user',
         PRIVATE_KEY_FILE='/etc/certs/snowflake_rsa_key.p8',
         DATABASE='ANALYTICS', WAREHOUSE='TRANSFORM_WH');

-- Query with full SQL pushdown
SELECT account_id, SUM(revenue) AS total
FROM   sf.ORDERS
GROUP  BY account_id
HAVING total > 100000
QUALIFY ROW_NUMBER() OVER (PARTITION BY region ORDER BY total DESC) = 1;

-- Copy into ETL-SQL variable table
COPY INTO #top_accounts FROM sf.ORDERS WHERE status = 'CLOSED';
```

**Supported Snowflake-specific SQL:**

| Feature | Notes |
| :--- | :--- |
| `QUALIFY` | Filter on window functions without a sub-query |
| `ILIKE` / `RLIKE` | Case-insensitive `LIKE`; regex `LIKE` |
| `IFF(cond, t, f)` | Inline conditional (equivalent to `CASE WHEN`) |
| `NVL` / `NVL2` / `ZEROIFNULL` / `NULLIFZERO` | Null-substitution functions |
| `TRY_CAST` / `TRY_TO_DATE` / `TRY_TO_NUMBER` | Safe type casts that return `NULL` on failure |
| `ARRAY_AGG` / `OBJECT_CONSTRUCT` | Semi-structured aggregation |
| `FLATTEN` / `LATERAL` | Lateral flattening of arrays/variants |
| `PARSE_JSON` / `GET_PATH` | JSON access |
| `SAMPLE` / `TABLESAMPLE` | Statistical sampling |
| `APPROX_COUNT_DISTINCT` / `APPROX_PERCENTILE` | Approximate aggregates |

The keywords `TOP` and `NOLOCK` are excluded (T-SQL only). Use `LIMIT` for row capping.

---

### 2.7 BigQuery (`BIGQUERY`)

Native connector for Google BigQuery. Uses the BigQuery REST API (not ADO.NET). Supports full Standard SQL pushdown, schema introspection, streaming inserts, and batch reads. Two authentication modes: service-account JSON key file (`CREDENTIAL_FILE`) or Application Default Credentials (ADC / workload identity) when no credential file is provided.

> [!IMPORTANT]
> BigQuery does **not** support traditional RDBMS transactions. All DML statements (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`) are auto-committed per statement. Using `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` with a BigQuery connection has no effect.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PROJECT_ID` | GCP project ID | Yes |
| `DATASET` | Default dataset (equivalent to schema). Required for schema introspection and write operations. | No |
| `CREDENTIAL_FILE` | Path to service account JSON key file. Omit to use ADC (Cloud Run, GKE workload identity, `gcloud auth application-default login`). | No |
| `LOCATION` | BigQuery job location: `US`, `EU`, `us-central1`, etc. Defaults to `US`. | No |

*Examples:*
```sql
-- Service-account auth with a fixed dataset
CREATE CONNECTION bq AS BIGQUERY(PROJECT_ID='my-gcp-project', DATASET='analytics',
         CREDENTIAL_FILE='/etc/sa/bigquery-sa.json', LOCATION='US');

-- ADC (workload identity / developer machine)
CREATE CONNECTION bq AS BIGQUERY(PROJECT_ID='my-gcp-project', DATASET='analytics');

-- SQL pushdown — BigQuery Standard SQL sent directly
SELECT account_id, SUM(revenue) AS total
FROM   bq.orders
WHERE  status = 'CLOSED'
GROUP  BY account_id
QUALIFY ROW_NUMBER() OVER (PARTITION BY region ORDER BY total DESC) = 1
LIMIT  100;

-- Three-part table name (project.dataset.table)
SELECT * FROM bq.`myproject.staging.raw_events` LIMIT 1000;

-- Copy into ETL-SQL variable table
COPY INTO #events FROM bq.events WHERE event_date >= '2024-01-01';
```

**BigQuery SQL dialect notes:**

| Feature | Notes |
| :--- | :--- |
| Identifiers | Backtick-quoted: `` `project.dataset.table` `` |
| `QUALIFY` | Filter on window functions without a sub-query |
| `SAFE_CAST` | Type cast returning `NULL` on failure (instead of error) |
| `COUNTIF(cond)` | Conditional aggregate — equivalent to `COUNT(CASE WHEN cond THEN 1 END)` |
| `APPROX_COUNT_DISTINCT` | Approximate distinct count (HLL++ algorithm) |
| `UNNEST(array)` | Flatten array to rows |
| `STRUCT(...)` | Inline record construction |
| `TO_JSON_STRING` | Serialize a value to a JSON string |
| `GENERATE_ARRAY(start, end, step)` | Produce an integer array |

T-SQL keywords `TOP`, `NOLOCK`, `ISNULL`, `GETDATE`, and `SYSDATE` are excluded. Use `LIMIT`, `IFNULL`, and `CURRENT_DATETIME()` respectively.

**Write behavior:** `COPY INTO` uses BigQuery streaming inserts (low latency, no staging). `COPY INTO … APPEND=FALSE` truncates via `TRUNCATE TABLE` before inserting.

### 2.8 Neo4j (`NEO4J`)
Aliases: `NEO`

Graph database connector supporting property graph ingestion and Cypher pass-through querying.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `CONNECTION_STRING` / `URI` | Bolt/Neo4j connection URI (e.g. `bolt://localhost:7687`) | Yes (structured) |
| `USER` | Authentication username | No |
| `PASSWORD` | Authentication password | No |
| `DATABASE` | Target database name (Default: `neo4j`) | No |
| `TIMEOUT_SECONDS` | Connection and query timeout limit in seconds (Default: `30`) | No |
| `HOST` | Server host name (alternative to connection string) | No |
| `PORT` | Server port (Default: `7687`) | No |
| `PROTOCOL` | URI scheme when `HOST`/`PORT` are used (Default: `bolt`) | No |
| `KEY_COLUMNS` | Comma-separated properties used to `MERGE` nodes or relationships instead of always `CREATE` | No |
| `FROM_LABEL` / `TO_LABEL` | Source/target node labels for `EDGE_<TYPE>` writes that use `_from_key` and `_to_key` | No |
| `FROM_KEY_COLUMN` / `TO_KEY_COLUMN` | Source/target node property names matched against `_from_key` and `_to_key` (Default: `id`) | No |
| `SKIP_MISSING_ENDPOINTS` | `TRUE` to skip edge rows with missing or unmatched endpoints instead of failing (Default: `FALSE`) | No |
| `SCHEMA_SAMPLE_SIZE` | Rows sampled for virtual table schema discovery; `0` scans all rows (Default: `1000`) | No |

`USER` and `PASSWORD` are passed to the Neo4j driver as an auth token and are not embedded in the stored connection URI.

Regular `SELECT` statements against `graph.NODE_*` and `graph.EDGE_*` read through the connector's virtual table layer. Use `EXECUTE graph BEGIN ... END` when you want native Cypher pass-through.
Truncating a table-scoped source such as `graph.NODE_CUSTOMER` deletes only that label; truncating the root connection deletes the whole graph.
`BEGIN TRANSACTION` enlists the Neo4j connection for graph writes, table-scoped truncates, and native Cypher executed through the connection. `COMMIT` persists those graph changes; `ROLLBACK` discards them.
Set `SCHEMA_SAMPLE_SIZE=0` only when complete sparse-property discovery matters more than the cost of scanning every node or relationship of the requested virtual table.

**Virtual Schema Mapping:**
Graph entities are mapped to virtual tables:
- **`NODE_<LABEL>`**: Virtual node table for the specified node label. Includes system columns `_id` (element ID) and `_labels` (comma-separated labels). Set `KEY_COLUMNS` for stable upserts via Cypher `MERGE`.
- **`EDGE_<TYPE>`**: Virtual relationship table for the specified relationship type. Includes system columns `_id` (relationship ID), `_from_id` (source element ID), `_to_id` (target element ID), `_from_label`, and `_to_label`. For portable ETL loads, provide `_from_key` and `_to_key` plus `FROM_LABEL`/`TO_LABEL` and optional key column options.

**Write Behavior:**
- Ingesting into `NODE_<LABEL>` or `EDGE_<TYPE>` uses parameterized `UNWIND` Cypher templates.
- If `KEY_COLUMNS` is set, writes use `MERGE` for idempotent upserts; otherwise writes use `CREATE`.
- Edge writes fail by default when endpoint columns are missing or endpoint matches are not found. Set `SKIP_MISSING_ENDPOINTS=TRUE` only when intentionally dropping those edge rows.
- `DBNull.Value` is written as `NULL`; dates/times and GUIDs are stored as strings; nested maps/rows are stored as JSON text.
- If `APPEND=FALSE` (the default), the delete-and-load operation runs inside a single Neo4j write transaction so failures roll back the replacement.
- Inside an engine `BEGIN TRANSACTION`, writes/truncates/native Cypher use the enlisted Neo4j transaction instead of their own per-operation transaction.
- In `SET WHAT_IF ON`, raw mutating Cypher in `EXECUTE` is skipped.

*Examples:*
```sql
-- Ingest customers as graph nodes
CREATE CONNECTION graph AS NEO4J(
    URI='bolt://localhost:7687',
    USER='neo4j',
    PASSWORD='password',
    KEY_COLUMNS='customer_id'
);
INSERT INTO graph.NODE_CUSTOMER (customer_id, name, city)
SELECT customer_id, name, city FROM #staging;

-- Native Cypher pass-through
DECLARE @minAge INT = 21;
EXECUTE graph INTO #fof_network WITH (@minAge)
BEGIN
    MATCH (p:Person)-[:FRIEND_OF]->()-[:FRIEND_OF]->(fof:Person)
    WHERE p.age >= ?1
    RETURN p.name AS source_name, fof.name AS fof_name
END;
```

---

## 3. Flat File & Document Connectors

### 3.1 Flat Files (`FLATFILE`)
Aliases: `CSV`, `TSV`

General-purpose connector for delimited and fixed-width text files.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `DELIMITER` | Column separator: `COMMA`, `PIPE`, `TAB`, `SEMICOLON`, `COLON`, `TILDE`, or a literal char (Default: `COMMA`) | No |
| `ROW_DELIMITER` | Row separator: `LF`, `CR`, `CRLF`, or a literal char (Default: `CRLF`) | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (Default: `ON`) | No |
| `TEXT_QUALIFIER` | Quote character: `DOUBLEQUOTE`, `SINGLEQUOTE`, or a literal char | No |
| `ESCAPE_CHAR` | Character used to escape delimiters within fields (e.g. `'\\'`) | No |
| `ENCODING` | `UTF8`, `ANSI`, `UTF16`, `LATIN1`, `UNICODE` (Default: `UTF8`) | No |
| `CULTURE` | Locale for date/number parsing (e.g. `en-US`, `de-DE`) | No |
| `NULL_AS` | How nulls are represented: `NULL`, `EMPTY`, `BACKSLASH_N` | No |
| `DATE_FORMAT` | Custom date parsing pattern (e.g. `'yyyy-MM-dd'`) | No |
| `START_AT` | 1-based line number to start reading | No |
| `END_AT` | 1-based line number to stop reading | No |
| `TRIM` | `ON`/`OFF` — remove leading/trailing whitespace from fields | No |
| `COUNT_AT_END` | `ON`/`OFF` — validate row count against a trailer record (Default: `OFF`) | No |
| `STRICT_SCHEMA` | `ON`/`OFF` — enforce column count matching (Default: `OFF`) | No |
| `FORMAT` | `DELIMITED` (Default) or `FIXED` | No |
| `TEMPLATE` | Name of a `#temp` table defining fixed-width offsets (Required if `FORMAT=FIXED`) | Conditional |
| `COMPRESS` | `ON`/`OFF` — transparent GZip read/write (Default: `OFF`) | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | Hash algorithm: `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `FLATFILE` connection via `SELECT` the table name is `FILE` and the columns are named based on the header row in the file or if there is no header row then the columns are named `Column1`, `Column2`, ...

*Examples:*
```sql
-- Pipe-delimited with explicit encoding
CREATE CONNECTION csv_in AS FLATFILE(PATH='C:\Data\employees.csv', HEADER=ON, DELIMITER=PIPE, ENCODING=UTF8);

-- Encrypted and GZip-compressed
CREATE CONNECTION secure_file AS FLATFILE('C:\Data\payroll.csv.gz', COMPRESS=ON, ENCRYPT=ON, PASSWORD='s3cr3t');

-- European locale with semicolon delimiter and custom date format
CREATE CONNECTION eu_data AS FLATFILE('C:\Data\german_sales.csv', DELIMITER=SEMICOLON, CULTURE='de-DE', DATE_FORMAT='dd.MM.yyyy');

-- Skip header and first 2 data rows, stop at row 1000
CREATE CONNECTION paged AS FLATFILE('C:\Data\big.csv', HEADER=ON, START_AT=3, END_AT=1000);
```

#### Fixed-Width Files

To read a fixed-width file, define a template table that specifies the width of each field. The engine slices each line using the declared widths.

**Width rules:**
- `VARCHAR(N)` / `CHAR(N)` / `NVARCHAR(N)` — engine uses their `N` as the field width automatically.
- `/* @width: N */` metadata comment — explicitly overrides the data type width.

```sql
-- 1. Define the layout
CREATE TABLE #EmpLayout (
    ID      INT          /* @width: 5 */,
    Name    VARCHAR(20),          -- width = 20 from VARCHAR length
    Dept    CHAR(3),              -- width = 3 from CHAR length
    Active  BIT          /* @width: 1 */
);

-- 2. Create the connection
CREATE CONNECTION fixed_emp AS FLATFILE('employees.dat', FORMAT='FIXED', TEMPLATE=#EmpLayout, HEADER=OFF, TRIM=ON);

-- 3. Query as normal
SELECT * FROM fixed_emp;
```

> [!IMPORTANT]
> When `FORMAT='FIXED'`, the `TEMPLATE` option is mandatory. The engine raises an error if any column width cannot be determined.

---

### 3.2 Excel (`EXCEL`)
Aliases: `XLSX`, `XLS`

Reads and writes Microsoft Excel workbooks (`.xlsx`, `.xls`, `.xlsb`).

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the workbook | Yes (structured) |
| `SHEET` | Target sheet name (Default: first sheet) | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (Default: `ON`) | No |
| `RANGE` | Explicit cell range to read (e.g. `'A1:F500'`) | No |
| `COMPRESS` | `ON`/`OFF` — GZip the output file after writing | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `EXCEL` connection via `SELECT` the table name is `FILE` and the columns are named based on the header row in the file or if there is no header row then the columns are named `Column1`, `Column2`, ...

*Examples:*
```sql
-- Specific sheet and range
CREATE CONNECTION xl_src AS EXCEL('C:\Reports\Q4.xlsx', SHEET='Summary', HEADER=ON, RANGE='A1:F500');

-- Write an encrypted workbook
CREATE CONNECTION xl_out AS EXCEL(PATH='C:\Secure\payroll.xlsx', ENCRYPT=ON, PASSWORD='safe_pass');
```

---

### 3.3 JSON (`JSON`)
Document extraction with JSONPath addressing for nested data.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | JSONPath to the root data array (e.g. `$.data.orders`) | No |
| `ENCODING` | Character encoding (Default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — transparent GZip support | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `JSON` connection via `SELECT` the table name is `FILE`.

*Examples:*
```sql
-- Drill into a nested array
CREATE CONNECTION json_src AS JSON('C:\Data\orders.json', ROOT_PATH='$.data.orders');

-- Compressed JSON
CREATE CONNECTION json_gz AS JSON(PATH='C:\Data\events.json.gz', COMPRESS=ON);
```

---

### 3.4 XML (`XML`)
Document extraction with XPath addressing for nested elements.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | XPath to the repeating element (e.g. `/Catalog/Book`) | No |
| `ENCODING` | Character encoding (Default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — transparent GZip support | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `XML` connection via `SELECT` the table name is `FILE`.

*Examples:*
```sql
-- XPath root selector
CREATE CONNECTION xml_src AS XML('C:\Data\catalog.xml', ROOT_PATH='/Catalog/Product');

-- Encrypted XML archive
CREATE CONNECTION xml_vault AS XML(PATH='C:\Vault\archive.xml', ENCRYPT=ON, PASSWORD='vault_pass');
```

---

### 3.5 Parquet (`PARQUET`)
Apache Parquet columnar format. Ideal for high-throughput analytics and interoperability with Spark, Hive, and data lake systems.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `COMPRESSION` | `SNAPPY` (Default), `GZIP`, `LZO`, `BROTLI`, `LZ4`, `ZSTD`, `UNCOMPRESSED` | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

*Examples:*
```sql
-- Write a Snappy-compressed Parquet file (default)
CREATE CONNECTION pq_out AS PARQUET(PATH='C:\Data\output.parquet');

-- Maximum compression for archival
CREATE CONNECTION pq_archive AS PARQUET('C:\Archive\data.parquet', COMPRESSION=ZSTD);
```

---

### 3.6 Avro (`AVRO`)
Apache Avro format. Schema is embedded within the file. Optionally reference an external `.avsc` schema file.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `SCHEMA_FILE` | Path to an external `.avsc` Avro schema file | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

*Examples:*
```sql
-- Read Avro with an external schema definition
CREATE CONNECTION avro_src AS AVRO('C:\Data\events.avro', SCHEMA_FILE='C:\Schemas\events.avsc');
```

### 3.7 Local Directory (`DIRECTORY`)
Treats a local filesystem folder as a data source for file management operations (`COPY FILE`, `DELETE FILE`, etc.) and directory listing via `SELECT`.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute directory path | Yes (structured) |
| `CREATE` | `ON`/`OFF` — create the directory if it doesn't exist (Default: `ON`) | No |

*Examples:*
```sql
CREATE CONNECTION data_dir AS DIRECTORY('C:\Data\Incoming', CREATE=ON);

-- List all files in the directory as a result set
SELECT FileName, Size, LastModified FROM data_dir;
```

#### Result Set Schema
When querying a `DIRECTORY` connection via `SELECT` the following columns are returned:
- `FileName` (STRING): Filename with extension.
- `Path` (STRING): Absolute path to the file.
- `Extension` (STRING): File extension (including dot).
- `Size` (DECIMAL): File size in bytes.
- `LastModified` (DATETIME): Last write time.
- `IsReadOnly` (BIT): `TRUE` if the file is read-only.
- `CreationTime` (DATETIME): Time the file was created.

---

## 4. Remote & Cloud Protocol Connectors

### 4.1 SFTP / SSH (`SFTP`)
Aliases: `SSH`

Secure File Transfer Protocol over SSH. Supports password and key-pair authentication (mutually exclusive).

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server domain or IP address | Yes (structured) |
| `PORT` | Listening port (Default: `22`) | No |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password — use for password auth only | No |
| `KEYFILE` | Path to the private SSH key — use for key auth only | No |
| `PASSPHRASE` | Passphrase for the private key (if set) | No |

> [!CAUTION]
> `PASSWORD` and `KEYFILE` are mutually exclusive. Providing both will cause an authentication error.

*Examples:*
```sql
-- Password authentication
CREATE CONNECTION sftp_pwd AS SFTP(HOST='sftp.example.com', USER='admin', PASSWORD='s3cr3t');

-- Key-pair authentication (recommended for production)
CREATE CONNECTION sftp_key AS SFTP('sftp.example.com', USER='deploy', KEYFILE='/home/etl/.ssh/id_rsa', PASSPHRASE='keypass');
```

---

### 4.2 FTP (`FTP`)
Aliases: `FTP_CONN`

Legacy File Transfer Protocol. Supports active and passive mode depending on the server.

> [!NOTE]
> `FTPS` (FTP over SSL/TLS) is treated as an alias token at parse time but uses the same connector. Provide `USE_SSL=TRUE` in the connection string if your server requires implicit FTPS.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | FTP server address or IP | Yes (structured) |
| `PORT` | Listening port (Default: `21`) | No |
| `USER` | Login username | No |
| `PASSWORD` | Login password | No |

*Examples:*
```sql
-- Structured
CREATE CONNECTION ftp_src AS FTP(HOST='ftp.example.com', USER='ftpuser', PASSWORD='ftppass');

-- Traditional
CREATE CONNECTION ftp_legacy AS FTP('ftp.example.com', USER='ftpuser', PASSWORD='ftppass');
```

---

### 4.3 Azure Blob Storage (`AZURE_BLOB`)
Aliases: `BLOB`

Cloud storage connector for reading and writing files in Azure Blob Storage containers.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `CONTAINER` | Target blob container name | Yes |
| `ACCOUNT_NAME` | Azure storage account name | No |
| `ACCOUNT_KEY` | Azure storage account key | No |

> [!NOTE]
> You can provide a full SAS connection string in the traditional syntax, or use `ACCOUNT_NAME` + `ACCOUNT_KEY` in structured syntax.

*Examples:*
```sql
-- Full connection string (SAS or AccountKey)
CREATE CONNECTION cloud AS AZURE_BLOB('DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=abc...', CONTAINER='backup-archive');

-- Structured with account credentials
CREATE CONNECTION cloud_struct AS AZURE_BLOB(ACCOUNT_NAME='myaccount', ACCOUNT_KEY='abc...', CONTAINER='raw-data');
```

---

### 4.4 REST API (`API`)
Aliases: `REST`, `HTTP`

Universal connector for web services and REST APIs returning JSON data.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `URL` | The endpoint URL | Yes |
| `METHOD` | HTTP method: `GET`, `POST`, `PUT`, `DELETE` (Default: `GET`) | No |
| `AUTH_TYPE` | Authentication mode: `NONE`, `BASIC`, `BEARER`, `APIKEY` (Default: `NONE`) | No |
| `USER` | Username (for `BASIC` auth) | No |
| `PASSWORD` | Password (for `BASIC` auth) | No |
| `TOKEN` | Secret token (for `BEARER` or `APIKEY` auth) | No |
| `HEADER_NAME` | Header name for `APIKEY` auth (e.g. `X-API-Key`) | No |
| `ROOT_PATH` | JSONPath to the data array within the response (e.g. `$.items`) | No |
| `BODY` | JSON request body for `POST`/`PUT` requests | No |
| `PAG_TYPE` | Pagination style: `NONE`, `OFFSET` (Default: `NONE`) | No |
| `PAG_LIMIT` | Batch size / page size for paginated APIs | No |

*Examples:*
```sql
-- Public GitHub API — array is the root response
CREATE CONNECTION github_issues AS API(URL='https://api.github.com/repos/microsoft/terminal/issues', ROOT_PATH='$');

SELECT title, created_at FROM github_issues;

-- Bearer token authentication
CREATE CONNECTION my_api AS API(URL='https://api.example.com/v1/customers',
         AUTH_TYPE='BEARER',
         TOKEN='sk_live_abc123');

-- APIKEY header auth
CREATE CONNECTION weather AS API(URL='https://api.weather.com/data',
         AUTH_TYPE='APIKEY',
         TOKEN='my_api_key_value',
         HEADER_NAME='X-API-Key');

-- POST with a JSON body
CREATE CONNECTION submit AS API(URL='https://api.example.com/events',
         METHOD='POST',
         AUTH_TYPE='BEARER',
         TOKEN='tok_live_xyz',
         BODY='{"type":"etl_run","status":"complete"}');

-- Paginated API with OFFSET-style paging
CREATE CONNECTION pages AS API(URL='https://api.example.com/records',
         ROOT_PATH='$.data',
         PAG_TYPE='OFFSET',
         PAG_LIMIT=100);
```

---

### 4.5 Email (`SMTP`)
Aliases: `EMAIL`

Outbound-only email connector used with the `SEND EMAIL` statement.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PORT` | SMTP server port (Default: `25`) | No |
| `USERNAME` | Authentication username | No |
| `PASSWORD` | Authentication password | No |
| `USE_SSL` | Enable TLS/SSL (`TRUE`/`FALSE`, Default: `FALSE`) | No |
| `DEFAULT_FROM` | Default sender address when `FROM` is omitted in `SEND EMAIL` | No |

*Examples:*
```sql
-- Gmail with TLS
CREATE CONNECTION mailer AS SMTP('smtp.gmail.com', PORT=587, USERNAME='alerts@example.com', PASSWORD='apppassword',
         USE_SSL=TRUE, DEFAULT_FROM='alerts@example.com');

SEND EMAIL
    TO 'ops@example.com'
    SUBJECT 'Nightly Load Complete'
    BODY 'All records processed.'
    AT mailer;
```

---

### 4.6 SharePoint (`SHAREPOINT`)
Aliases: `SP`

Manages files in SharePoint Document Libraries (remote file system operations) and reads/writes SharePoint Lists.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `AUTH_MODE` | Authentication mode: `INTEGRATED`, `AD_WINDOWS`, `ENTRA_ID`, `ADFS` (Default: `INTEGRATED`) | No |
| `USER` | Domain account username or service account (for `AD_WINDOWS` and `ADFS`) | No |
| `PASSWORD` | Password (for `AD_WINDOWS` and `ADFS`) | No |
| `DOMAIN` | Domain name (for `AD_WINDOWS` and `ADFS`) | No |
| `CLIENT_ID` | Microsoft Entra ID Application Client ID (for `ENTRA_ID`) | No |
| `CLIENT_SECRET` | Microsoft Entra ID Application Client Secret (for `ENTRA_ID`) | No |
| `TENANT_ID` | Microsoft Entra ID Tenant ID/Directory ID (for `ENTRA_ID`) | No |
| `DOCUMENT_LIBRARY` | Target Document Library path/title (Default: `Shared Documents`) | No |
| `LIST_NAME` | Default list title for list queries | No |

> [!IMPORTANT]
> - `CLIENT_SECRET` and `PASSWORD` should always be encrypted using `ENC:` string values.
> - Plaintext secrets in `CLIENT_SECRET` or `CLIENTSECRET` will trigger a linter warning.
> - When using `AUTH_MODE = 'ENTRA_ID'`, the options `TENANT_ID`, `CLIENT_ID`, and `CLIENT_SECRET` are mutually required.

*Examples:*
```sql
-- Client Credentials (Entra ID - Recommended for Cloud)
CREATE CONNECTION sp_cloud AS SHAREPOINT('https://tenant.sharepoint.com/sites/Finance',
         AUTH_MODE     = 'ENTRA_ID',
         TENANT_ID     = '00000000-0000-0000-0000-000000000000',
         CLIENT_ID     = '11111111-1111-1111-1111-111111111111',
         CLIENT_SECRET = ENC:U2FsdGVkX1+...);

-- Domain credentials (On-Premises / AD_WINDOWS)
CREATE CONNECTION sp_onprem AS SHAREPOINT('https://sharepoint.local/sites/HR',
         AUTH_MODE = 'AD_WINDOWS',
         USER      = 'sp_service',
         PASSWORD  = ENC:U2FsdGVkX1+...,
         DOMAIN    = 'CORP');

-- Integrated authentication
CREATE CONNECTION sp_integrated AS SHAREPOINT('https://tenant.sharepoint.com/sites/IT',
         AUTH_MODE = 'INTEGRATED');
```

---

## 5. Development & Testing: `MOCKDB`

ETL-SQL provides a built-in, zero-configuration in-memory database for script development and testing. No credentials, no server, no configuration required.

```sql
CREATE CONNECTION <name> AS MOCKDB();
```

### Pre-populated Tables

| Table | Columns |
| :--- | :--- |
| `Users` | `UserID`, `UserName`, `Email`, `ExternalID`, `RegistrationDate`, `PreciseTime`, `LastLoginOffset` |
| `Products` | `ProductID`, `ProductName`, `Category`, `Cost`, `Price`, `StockLevel`, `Discontinued`, `WeightGrams`, `SkidGuid` |
| `Orders` / `Sales` | `SaleID`, `OrderDate`, `CustomerID`, `ProductID`, `Quantity`, `UnitPrice`, `Total`, `Region`, `ShipTimeOffset`, `ProcessDuration` |
| `Employee` | `EmpID`, `FirstName`, `LastName`, `Name`, `DeptID`, `Salary`, `HireDate`, `ManagerID`, `Status`, `Active`, `GlobalID` |
| `departments` | `DeptID`, `DeptName`, `Budget` |

All tables are pre-seeded with sample rows. `INSERT`, `UPDATE`, and `DELETE` operations are accepted but **do not persist** between sessions.

*Example:*
```sql
CREATE CONNECTION m AS MOCKDB();

SELECT u.UserName, o.Total
INTO #UserOrders
FROM m.Users AS u
JOIN m.Orders AS o ON u.UserID = o.CustomerID;

-- Test an EXECUTE block
EXECUTE m INTO #emp
BEGIN
    SELECT EmpID, Name FROM Employee WHERE Active = 1;
END
```

> [!WARNING]
> `MOCKDB` is strictly for development and testing. Do not use it in production scripts.

---

## 6. Security Utilities

### 6.1 `USE PASSWORD`
Sets the master password for the current session used to decrypt `ENC:` connection strings.

```sql
USE PASSWORD = 'myMasterSecret';
CREATE CONNECTION db AS MSSQL('ENC:U2FsdGVkX1+...');
```

> [!NOTE]
> This is the **session master password**, not a connector credential. It is used only for `ENC:` string decryption.

### 6.2 `CREATE SSH_KEY_PAIR`
Generates an SSH key pair (public and private) at the specified directory. Supports SQL-style and function-style syntax.

*SQL Style (with named options):*
```sql
CREATE SSH_KEY_PAIR '<directory_path>'
    [WITH(BITS=2048, ALGORITHM='RSA', PASSPHRASE='pwd', COMMENT='comment')];
```

*Function Style (positional):*
```sql
SSH_KEY_PAIR('<directory_path>' [, bits, 'algorithm', 'passphrase', 'comment']);
```

| Option | Description | Default |
| :--- | :--- | :--- |
| `BITS` | Key size in bits (2048, 3072, 4096 for RSA; 256, 384, 521 for ECDSA) | `2048` |
| `ALGORITHM` | `RSA`, `ECDSA` | `RSA` |
| `PASSPHRASE` | Passphrase to encrypt the private key | *(none)* |
| `COMMENT` | Comment embedded in the public key file | *(none)* |

*Examples:*
```sql
-- Standard RSA key
CREATE SSH_KEY_PAIR 'C:\Keys\id_rsa';

-- 4096-bit RSA with passphrase
CREATE SSH_KEY_PAIR 'C:\Keys\id_rsa_prod'
    WITH(BITS=4096, PASSPHRASE='StrongPassword123!', COMMENT='Production ETL Service Account');

-- ECDSA key
SSH_KEY_PAIR('C:\Keys\id_ecdsa', 384, 'ECDSA', 's3cr3t');
```

---

## 7. Connection Lifecycle Commands

### 7.1 `DROP CONNECTION`
Closes and removes a connection from the current session. Frees connection pool slots and file handles.

```sql
DROP CONNECTION IF EXISTS legacy_db;
```

### 7.2 `ALTER CONNECTION`
Modifies properties of an existing connection. All unspecified properties are preserved.

```sql
ALTER CONNECTION remote_srv WITH(PASSWORD='new_rotated_password');
```

### 7.3 `CREATE OR ALTER CONNECTION`
Upserts a connection. If it exists, it is completely rebuilt with only the new options provided (previous options are NOT preserved).

```sql
CREATE OR ALTER CONNECTION remote_srv AS MSSQL('Server=db;Database=DW;', TABLE='dbo.Config');
```

### 7.4 `SHOW CONNECTIONS`
Lists all active connections in the current session.

```sql
SHOW CONNECTIONS [INTO #temp];
```

### 7.5 `HELP CONNECTION <type>`
Displays the connector's supported options and authentication patterns directly in the messages panel.

```sql
HELP CONNECTION MSSQL;
HELP CONNECTION SFTP;
HELP CONNECTION FLATFILE;
```

---

## 8. Admin Service Connectors

Admin service connectors do not transfer data — they control remote services. Statements inside `EXECUTE <alias> BEGIN...END` blocks are dispatched to the service's REST API rather than compiled to SQL.

### 8.1 Report Portal (`REPORTPORTAL`)
Alias: `REPORT_PORTAL`

Connects to an ETL-SQL Report Portal service for scripted administration: user/group management, folder ACL, report publishing, dataset refresh, snapshots, and more.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Portal base URL (e.g. `http://portal-server:5000`) | Yes |
| `PORT` | Override port when `HOST` has no port | No |
| `USER` | Portal admin username | Yes |
| `PASSWORD` | Portal admin password (use `ENC:` in production) | Yes |

```sql
CREATE CONNECTION portal AS REPORTPORTAL(HOST     = 'http://portal.corp.example:5000',
         USER     = 'admin',
         PASSWORD = ENC:U2FsdGVkX1+...);

EXECUTE portal BEGIN
    -- User management
    CREATE USER 'jsmith' WITH EMAIL='j@corp.com', ROLE='Viewer';
    ALTER USER 'jsmith' SET ROLE = 'Editor';
    DROP USER 'jsmith';

    -- Group management
    CREATE GROUP 'DataTeam' WITH DESCRIPTION='Data Engineering';
    ADD USER 'alice' TO GROUP 'DataTeam';
    DROP GROUP 'DataTeam';

    -- Folder management & ACL
    CREATE FOLDER '/Finance/Reports';
    ALTER FOLDER '/Finance/Reports' RENAME TO 'Archived Reports';
    ALTER FOLDER '/Finance/Reports' SET PARENT = '/Archive';
    GRANT VIEW ON FOLDER '/Finance/Reports' TO GROUP 'DataTeam';
    REVOKE VIEW ON FOLDER '/Finance/Reports' FROM GROUP 'DataTeam';
    DROP FOLDER '/Finance/Reports' CASCADE;

    -- Report lifecycle
    PUBLISH REPORT 'Monthly Sales'
        FROM SCRIPT 'reports/monthly_sales.rsql'
        IN FOLDER '/Finance/Reports';
    ALTER REPORT 'Monthly Sales' SET FOLDER = '/Finance/Archived';
    REFRESH REPORT 'Monthly Sales';
    REBUILD SNAPSHOT FOR REPORT 'Monthly Sales';
    DROP REPORT 'Monthly Sales' CASCADE;

    -- Dataset management
    REFRESH DATASET 'sales_ds' IN FOLDER '/Finance';
    ALTER DATASET 'sales_ds' IN FOLDER '/Finance'
        WITH SCHEDULE='0 2 * * *';
    DROP DATASET 'sales_ds' IN FOLDER '/Finance';

    -- Refresh jobs (routed to Orchestrator)
    CREATE REFRESH JOB FOR REPORT 'Monthly Sales'
        SCHEDULE '0 2 * * *' AT orch;
    DROP REFRESH JOB FOR REPORT 'Monthly Sales';

    -- Discovery
    SHOW USERS;
    SHOW REPORTS IN FOLDER '/Finance/Reports';
END;
```

> [!NOTE]
> JWT authentication is acquired automatically on first use and refreshed as needed. The `PASSWORD` value is never logged or stored in session state.

### 8.2 Orchestrator (`ORCHESTRATOR`)
Alias: `ORCH`

Connects to an ETL-SQL Orchestrator service for remote job management via API key authentication.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Orchestrator base URL (e.g. `http://orch-server:5001`) | Yes |
| `PORT` | Override port when `HOST` has no port | No |
| `API_KEY` | Orchestrator API key (use `ENC:` in production) | No |

```sql
CREATE CONNECTION orch AS ORCHESTRATOR(HOST    = 'http://orchestrator.corp.example:5001',
         API_KEY = ENC:U2FsdGVkX1+...);

EXECUTE orch BEGIN
    CREATE REFRESH JOB FOR REPORT 'Monthly Sales'
        SCHEDULE '0 2 * * *';
    DROP REFRESH JOB FOR REPORT 'Monthly Sales';
END;
```

---

### 8.3 Active Directory (`ACTIVE_DIRECTORY`)
Aliases: `AD`, `LDAP`

Connects to an Active Directory or LDAP server to perform user, group, and computer lookups. Standard SQL `WHERE` clauses (e.g. `sAMAccountName = 'smith'`) are parsed and translated dynamically into native LDAP filter queries.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server host name or IP address (e.g. `ldap.corp.com`) | Yes (structured) |
| `PORT` | Directory port (Default: `389` for LDAP, `636` for LDAPS) | No |
| `USE_SSL` | Enable SSL encryption / LDAPS connection (`TRUE`/`FALSE`) | No |
| `AUTH_MODE` | Authentication mode: `INTEGRATED`, `SIMPLE` (Basic auth over SSL), `NEGOTIATE` (negotiate credentials) (Default: `INTEGRATED`) | No |
| `USER` | Login username / Bind Distinguished Name (DN) | No |
| `PASSWORD` | Login password (use `ENC:` prefix) | No |
| `DOMAIN` | Domain name | No |
| `BASE_DN` | LDAP Search Base Distinguished Name (e.g. `OU=Users,DC=corp,DC=com`) | No |
| `FILTER_CONTEXT` | Scope context: `users`, `groups`, or `computers` (Default: `users`) | No |
| `FILTER` | Raw LDAP query filter (overrides `FILTER_CONTEXT` and standard AD parsing) | No |
| `ATTRIBUTES` | Comma-separated list of attributes to query | No |

> [!CAUTION]
> `AUTH_MODE = 'SIMPLE'` transmits credentials in plaintext unless `USE_SSL=TRUE` (LDAPS) is active. It is highly recommended to use `USE_SSL=TRUE` with simple binding.

*Examples:*
```sql
-- Search users with Negotiate auth over standard LDAP
CREATE CONNECTION ad_corp AS ACTIVE_DIRECTORY(
         HOST       = 'ldap.corp.example.com',
         BASE_DN    = 'DC=corp,DC=example,DC=com',
         AUTH_MODE  = 'NEGOTIATE',
| `AUTH_MODE` | Authentication mode: `INTEGRATED`, `SIMPLE` (Basic auth over SSL), `NEGOTIATE` (negotiate credentials) (Default: `INTEGRATED`) | No |
| `USER` | Login username / Bind Distinguished Name (DN) | No |
| `PASSWORD` | Login password (use `ENC:` prefix) | No |
| `DOMAIN` | Domain name | No |
| `BASE_DN` | LDAP Search Base Distinguished Name (e.g. `OU=Users,DC=corp,DC=com`) | No |
| `FILTER_CONTEXT` | Scope context: `users`, `groups`, or `computers` (Default: `users`) | No |
| `FILTER` | Raw LDAP query filter (overrides `FILTER_CONTEXT` and standard AD parsing) | No |
| `ATTRIBUTES` | Comma-separated list of attributes to query | No |

> [!CAUTION]
> `AUTH_MODE = 'SIMPLE'` transmits credentials in plaintext unless `USE_SSL=TRUE` (LDAPS) is active. It is highly recommended to use `USE_SSL=TRUE` with simple binding.

*Examples:*
```sql
-- Search users with Negotiate auth over standard LDAP
CREATE CONNECTION ad_corp AS ACTIVE_DIRECTORY(
         HOST       = 'ldap.corp.example.com',
         BASE_DN    = 'DC=corp,DC=example,DC=com',
         AUTH_MODE  = 'NEGOTIATE',
         USER       = 'domain_service',
         PASSWORD   = ENC:U2FsdGVkX1+...,
         DOMAIN     = 'CORP');

-- Query using AD connection
SELECT sAMAccountName, displayName, mail, memberOf
FROM ad_corp
WHERE sAMAccountName = 'jdoe';
```

---

## 9. Quick Reference Table

| Token | Aliases | Type | Pushdown | Transactional |
| :--- | :--- | :--- | :---: | :---: |
| `MSSQL` | `SQL`, `SQLSERVER` | Relational | ✓ | ✓ |
| `POSTGRES` | `NPSQL`, `PG` | Relational | ✓ | ✓ |
| `ORACLE` | — | Relational | ✓ | ✓ |
| `ODBC` | — | Relational | Varies | — |
| `NEO4J` | `NEO` | Graph | ✓ | ✗ |
| `MOCKDB` | — | In-memory | — | — |
| `FLATFILE` | `CSV`, `TSV` | File | — | — |
| `EXCEL` | `XLSX`, `XLS` | File | — | — |
| `JSON` | — | File | — | — |
| `XML` | — | File | — | — |
| `PARQUET` | — | File | — | — |
| `AVRO` | — | File | — | — |
| `API` | `REST`, `HTTP` | Protocol | — | — |
| `SFTP` | `SSH` | Protocol | — | — |
| `FTP` | `FTP_CONN`, `FTPS` | Protocol | — | — |
| `AZURE_BLOB` | `BLOB` | Protocol | — | — |
| `SMTP` | `EMAIL` | Protocol | — | — |
| `SHAREPOINT` | `SP` | Protocol | — | — |
| `REPORTPORTAL` | `REPORT_PORTAL` | Admin Service | — | — |
| `ORCHESTRATOR` | `ORCH` | Admin Service | — | — |
| `ACTIVE_DIRECTORY` | `AD`, `LDAP` | Admin Service | — | — |
| `DIRECTORY` | — | File | — | — |
