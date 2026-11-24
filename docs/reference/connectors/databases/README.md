# DATABASES Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [BIGQUERY](bigquery.md) | Native connector for Google BigQuery. Uses the BigQuery REST API (not ADO.NET). Supports full Standard |
| [MONGODB](mongodb.md) | Connects to a MongoDB document database using the official MongoDB.Driver. Querying a `MONGODB` |
| [MSSQL](mssql.md) | Connects to Microsoft SQL Server or Azure SQL Database. Supports full SQL pushdown, transactions, |
| [MYSQL](mysql.md) | Native connector for MySQL and MariaDB databases using the MySqlConnector driver. Supports full SQL |
| [NEO4J](neo4j.md) | Graph database connector supporting property-graph ingestion and Cypher pass-through querying via the |
| [ODBC](odbc.md) | Universal bridge for any source with a local ODBC driver. Supports both DSN-based and DSN-less |
| [ORACLE](oracle.md) | Connects to Oracle Database using either an **Easy Connect** host/service combination or a |
| [POSTGRES](postgres.md) | Connects to PostgreSQL databases using the Npgsql driver. Supports full SQL pushdown, schema |
| [SNOWFLAKE](snowflake.md) | Native connector for the Snowflake Cloud Data Platform. Supports full SQL pushdown, schema |
| [SQLITE](sqlite.md) | Connects to local or in-memory SQLite databases using the lightweight Microsoft.Data.Sqlite driver. |

## Capability Matrix

Every cell below is verified against the connector's `IDataSource` implementation. See each connector's **Write Path & Transactions** section for full details.

| Connector | Transactions | SQL Pushdown | Write / Bulk Path | Schema Introspection | `WHAT_IF` | Default Timeout | Auth Modes |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| [BIGQUERY](bigquery.md) | No (auto-commit per statement) | Yes (Standard SQL) | Streaming inserts (`InsertRowsAsync`, no replay) | Yes (`ListTables` + `INFORMATION_SCHEMA`, needs `DATASET`) | Skips writes, truncates & pushed DML | `1800s` | Service-account JSON (`CREDENTIAL_FILE`), ADC |
| [MONGODB](mongodb.md) | No | No (read-only JSON filter/pipeline via `EXECUTE ... BEGIN ... END`) | Batch `InsertManyAsync`, or `BulkWriteAsync` `ReplaceOneModel` upsert with `KEY_COLUMNS` (`APPEND=FALSE` drops collection) | Sampled (`ListCollectionNames` + first document) | Skips writes, drops & `EXECUTE` | `30s` | Username/password (`USER`/`PASSWORD` or URI, SCRAM) |
| [MSSQL](mssql.md) | Yes (`SqlTransaction`) | Yes (T-SQL; native same-conn `MERGE`) | `SqlBulkCopy` | Yes (catalog provider + `INFORMATION_SCHEMA`) | Skips writes, truncates & pushed DML | `30s` | `AUTH_MODE`: `SQL`, `WINDOWS` (`TRUSTED_CONNECTION`), `ENTRA_DEFAULT`, `ENTRA_MANAGED_IDENTITY`, `ENTRA_SERVICE_PRINCIPAL` |
| [MYSQL](mysql.md) | Yes (`MySqlTransaction`) | Yes (MySQL) | `MySqlBulkCopy` | Yes (catalog provider + `INFORMATION_SCHEMA`) | Skips writes, truncates & pushed DML | `30s` | Username/password (`USER`/`PASSWORD`), SSL (`SSL_MODE`) |
| [NEO4J](neo4j.md) | Yes (enlisted session tx / `ExecuteWriteAsync`) | No (Cypher via `EXECUTE ... BEGIN ... END`) | Batched Cypher `UNWIND` (`CREATE`, or `MERGE` with `KEY_COLUMNS`) | Sampled (`db.labels` + `SCHEMA_SAMPLE_SIZE=1000`) | Skips writes, truncates & mutating Cypher | `30s` | Basic auth (`USER`/`PASSWORD`), URI TLS (`+s`/`+ssc`) |
| [ODBC](odbc.md) | Yes (`OdbcTransaction`, driver-dependent) | Yes (driver-dependent) | Parameterized row-by-row `INSERT` in tx | Driver-dependent (`GetSchema`) | Skips writes, truncates & pushed DML | `30s` | DSN, `UID`/`USER` + `PASSWORD`/`PWD`, driver `AUTHMECH` |
| [ORACLE](oracle.md) | No (auto-commit; use PL/SQL in `EXECUTE`) | Yes (Oracle SQL) | `OracleBulkCopy` | Yes (`all_tables` / `all_views` + `ROWNUM = 0`) | Skips writes, truncates & pushed DML | `30s` | Easy Connect or TNS (`USER`/`PASSWORD`) |
| [POSTGRES](postgres.md) | Yes (`NpgsqlTransaction`) | Yes (PostgreSQL) | Text-mode `COPY ... FROM STDIN` | Yes (catalog provider + `INFORMATION_SCHEMA`) | Skips writes, truncates & pushed DML | `30s` | Username/password (`USER`/`PASSWORD`), SSL (`SSL_MODE`) |
| [SNOWFLAKE](snowflake.md) | Yes (shared session tx) | Yes (Snowflake SQL) | Parameterized row-by-row `INSERT` | Yes (`INFORMATION_SCHEMA`) | Skips writes, truncates & pushed DML | `1800s` | Username/password, RSA key-pair JWT (`PRIVATE_KEY_FILE` + optional `PRIVATE_KEY_PASSPHRASE`) |
| [SQLITE](sqlite.md) | Yes (`SqliteTransaction`, single-writer) | Yes (SQLite SQL) | Parameterized row-by-row `INSERT` in tx (`DELETE` on `APPEND=FALSE`) | Yes (catalog provider + `PRAGMA table_info`) | Skips writes, truncates & pushed DML | `30s` | File-system permissions (no DB password; `Mode=ReadOnly` via conn string) |

## Transient Retries

Database connectors use a global Polly retry policy configured in `appsettings.json` under `Connectors:Retry` (not per-connection options). Retries use exponential backoff with jitter on transient driver errors and timeouts.

```json
{
  "Connectors": {
    "Retry": {
      "MaxAttempts": 3,
      "BaseDelaySeconds": 1.0
    }
  }
}
```

- **`MaxAttempts`** — Maximum retry attempts after the initial failure (default: `3`; values `< 1` fall back to `3`).
- **`BaseDelaySeconds`** — Base delay in seconds for exponential backoff with jitter (default: `1.0`; negative values fall back to `1.0`).

**Connector coverage:**
- **Uses `Connectors:Retry`:** `BIGQUERY`, `MSSQL`, `MYSQL`, `ODBC`, `ORACLE`, `POSTGRES`, `SNOWFLAKE`.
- **Does not use `Connectors:Retry`:** `MONGODB` (relies on MongoDB driver server selection / retryable reads), `NEO4J` (relies on Neo4j driver transaction retries), and `SQLITE` (local file database; use `BUSY_TIMEOUT_MS` for lock contention).

## Related References

- [Database Native-to-Engine Type Mapping](../type-mapping.md) — how native types (`NUMBER`, `jsonb`, arrays, `VARIANT`, `datetimeoffset`, `STRUCT`, BSON, Cypher properties) land in the engine.
