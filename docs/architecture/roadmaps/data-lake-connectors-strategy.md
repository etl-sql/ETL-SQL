# Data Lake Connectors Strategy

> [!IMPORTANT]
> **Mixed shipped capability and future direction.** Native Snowflake and BigQuery connector behavior belongs in `docs/administration/platform/README.md` and `Docs/Architecture/Connectors.md`. Treat the raw object-storage/table-format sections as future strategy, not shipped feature reference.

**Status:** Partly implemented (BigQuery/Snowflake shipped; object-storage and open table format are strategic direction)

## What "Data Lake" Means Here

The term covers a wide range of products that are architecturally quite different. For ETL-SQL's purposes they fall into two distinct tiers that require different implementation approaches:

**Tier A — Cloud SQL Data Warehouses**

These speak ANSI SQL over a driver. Query engines, storage, and compute are managed by the provider. ETL-SQL submits SQL, gets results back. The connector pattern is nearly identical to existing database connectors.

**Tier B — Raw Object Storage + Open Table Formats**

Files living on S3, Azure Data Lake Storage (ADLS), or GCS, optionally organized into Iceberg, Delta Lake, or Hudi tables. Reading these without a query engine requires libraries that understand the columnar file format and table metadata protocol directly.

---

## What We Already Have

Before building anything new, two significant pieces are already in place:

| Existing capability | Covers |
| :--- | :--- |
| **ODBC connector** | Any data warehouse that publishes an ODBC driver: Snowflake, Redshift, Databricks, Azure Synapse, Trino, Dremio, and many others. If the driver is installed on the host machine, a `CREATE CONNECTION` with `TYPE = ODBC` connects today with zero new code. |
| **Parquet connector** | Local and network-accessible Parquet files — the primary storage format for data lakes and all Tier A warehouse exports. |
| **Avro connector** | Avro files — used by Kafka topics, Apache Iceberg metadata, and some Hive-era data lake layouts. |

This narrows the list of platforms that actually need new native connectors to those where ODBC is insufficient or impractical.

---

## Where ODBC Falls Short

ODBC covers the SQL execution path but leaves gaps in three areas:

### 1. Managed OAuth2 / Token-Based Authentication

ODBC connection strings are static at open time. They cannot:
- Exchange a service account JSON file for an access token (BigQuery / GCP pattern)
- Refresh a short-lived OAuth2 token mid-session
- Perform PKCE browser flows for interactive logins
- Generate a signed JWT from an RSA private key file (Snowflake key-pair auth)

For platforms where the only practical auth is token-based, a native connector is needed to manage the auth lifecycle.

### 2. LSP Schema Introspection

The language server uses `ISchemaProvider` to autocomplete table and column names in the editor. ODBC exposes `SQLTables`/`SQLColumns` but the results vary by driver and are often slow or incomplete. A native connector can use the platform's `INFORMATION_SCHEMA` directly and cache results correctly.

### 3. Platform-Specific SQL Dialect

A few platforms use SQL dialects that differ enough from the SQL ETL-SQL generates that the pushdown layer needs to know about them at query-build time:

- **BigQuery**: Uses backtick-quoted identifiers and `project.dataset.table` three-part notation. Standard ODBC pushdown will generate wrong SQL.
- **Snowflake** (minor): `QUALIFY` clause, `VARIANT` semi-structured type access — not blockers but worth handling natively.

---

## The Actual Build List

### Must-build native connectors

| Platform | Why ODBC is not enough |
| :--- | :--- |
| **Snowflake** | Key-pair JWT auth is impractical via ODBC connection string; extremely popular; `Snowflake.Data` NuGet is mature and well-supported. |
| **Google BigQuery** | Service account JSON / ADC auth is GCP-native and not expressible in an ODBC connection string. Three-part identifier notation requires pushdown layer awareness. |

### ODBC is sufficient (no new connector needed)

| Platform | ODBC driver | Notes |
| :--- | :--- | :--- |
| Amazon Redshift | Amazon Redshift ODBC driver | PostgreSQL-compatible; existing ODBC connector works as-is. |
| Azure Synapse Analytics | SQL Server ODBC / Azure AD driver | SQL Server-compatible; existing ODBC connector + Windows-integrated auth or connection string. |
| Databricks SQL Warehouse | Databricks ODBC driver | Personal access token goes in the connection string. Community Edition available for testing. |
| Trino / Presto | Trino ODBC driver or REST shim | Self-hostable in Docker for testing. |
| Dremio | Dremio ODBC driver | Community edition. |

### Already covered

| Platform / format | Existing connector |
| :--- | :--- |
| Parquet files (local, NFS, cloud-mounted) | Parquet connector |
| Avro files | Avro connector |

### DuckDB — reconsidered

DuckDB's main differentiation is not file reading (the Parquet connector handles that) but rather acting as an **in-process analytical SQL engine**: it can run complex GROUP BY / window functions / CTEs directly over Parquet/CSV files without first loading them into a `#temp` table. That use case is genuinely unique. However it is also the lowest urgency item — it is an ergonomics win, not a capability gap. It is included in the checklist at low priority.

---

## Authentication Model Extension

The existing `CREATE CONNECTION` stores host, port, username, and password. Native connectors for Snowflake and BigQuery need additional `OPTIONS` fields:

**Snowflake:**
```sql
-- Username + password
CREATE CONNECTION sf_prod AS SNOWFLAKE(
    HOST = 'myorg-myaccount.snowflakecomputing.com',
    DATABASE = 'ANALYTICS',
    SCHEMA = 'PUBLIC',
    WAREHOUSE = 'COMPUTE_WH',
    USERNAME = 'etlsql_svc',
    PASSWORD = @sf_password);

-- Key-pair authentication (recommended for service accounts)
CREATE CONNECTION sf_prod AS SNOWFLAKE(
    HOST = 'myorg-myaccount.snowflakecomputing.com',
    DATABASE = 'ANALYTICS',
    WAREHOUSE = 'COMPUTE_WH',
    USERNAME = 'etlsql_svc',
    PRIVATE_KEY_FILE = '/keys/rsa_key.p8');
```

**BigQuery:**
```sql
-- Service account JSON file
CREATE CONNECTION bq_prod AS BIGQUERY(
    PROJECT = 'my-gcp-project',
    CREDENTIAL_FILE = '/credentials/sa-key.json');

-- Application Default Credentials (workload identity, Cloud Run, etc.)
CREATE CONNECTION bq_prod AS BIGQUERY(PROJECT = 'my-gcp-project');
```

**ODBC-based data warehouse (existing syntax, shown for completeness):**
```sql
CREATE CONNECTION redshift_prod AS ODBC(
    DSN = 'RedshiftProd',
    USERNAME = 'etlsql_svc',
    PASSWORD = @rs_password);

CREATE CONNECTION databricks_prod AS ODBC(
    HOST = 'adb-xxxxx.azuredatabricks.net',
    DRIVER = 'Databricks ODBC Driver',
    HTTP_PATH = '/sql/1.0/warehouses/xxxxx',
    AUTH_TOKEN = @databricks_token);
```

Sensitive values (`PASSWORD`, auth tokens, `CREDENTIAL_FILE` path) reference variables so they can be injected at runtime and are stored encrypted in the connector registry.

---

## BigQuery Pushdown Dialect Handling

BigQuery uses a non-standard SQL dialect in two ways that affect ETL-SQL's pushdown layer:

**Identifier quoting:** Standard SQL uses double quotes; BigQuery uses backticks.
```sql
-- Standard SQL (what ETL-SQL generates today)
SELECT "region", SUM("revenue") FROM "sales_fact" GROUP BY "region"

-- BigQuery SQL (what the pushdown must emit)
SELECT `region`, SUM(`revenue`) FROM `my-project.analytics.sales_fact` GROUP BY `region`
```

**Three-part table names:** BigQuery tables are `project.dataset.table`, not `schema.table`. The connector must translate or the user must use fully-qualified names.

The `ISqlCompilerContext` interface already has a `QuoteIdentifier` abstraction — the BigQuery connector just needs to register a backtick-quoting implementation and handle the three-part name resolution.

---

## Testing Strategy

### No cloud account required

- **Snowflake:** `Snowflake.Data` supports an in-memory test mode (SnowflakeDbConnection with a mock transport). Unit tests can validate auth, connection string building, and schema introspection without a live account. Full integration tests use the 30-day free trial with CI secrets.
- **BigQuery:** `Google.Cloud.BigQuery.V2` can be tested against the BigQuery emulator (available as a Docker image: `ghcr.io/goccy/bigquery-emulator`). True integration tests use the BigQuery free tier (1 TB/month of queries) via a GCP service account stored in CI secrets.
- **ODBC-based connectors:** The existing ODBC connector's test coverage extends to these platforms by definition. Dialect-specific tests (complex window functions, CTEs) use a local Trino testcontainer (`trinodb/trino:latest` in Docker).
- **DuckDB:** In-process, zero setup, no credentials needed at any level.

### Integration test CI matrix

All integration tests are tagged `Category=Integration` and excluded from the default `dotnet test` run. They are run in a dedicated CI pipeline step with secrets:

| Connector | CI secret | Free option |
| :--- | :--- | :--- |
| Snowflake | `SNOWFLAKE_CONNECTION_STRING` | 30-day trial |
| BigQuery | `GCP_SA_KEY_JSON` | 1 TB/month free tier |
| Databricks (ODBC) | `DATABRICKS_TOKEN` | Community Edition |
| Trino (local) | *(none — Docker)* | Free, self-hosted |

---

## Connector Interface Considerations

Two small additions to the existing connector interface apply to all data warehouse connectors (native and ODBC-based when configured for a warehouse target):

### Timeout

Data warehouse queries can run for many minutes. The existing timeout defaults are calibrated for OLTP millisecond latency. Add `CommandTimeoutSeconds` to connector metadata, with a separate default for warehouse-type connectors:

```json
"Connectors": {
  "DataWarehouse": {
    "DefaultCommandTimeoutSeconds": 1800
  }
}
```

A per-connection override via `OPTIONS(TIMEOUT_SECONDS = n)` on `CREATE CONNECTION`.

### Read-only default

Analytics platforms are read-heavy by design. All data warehouse connectors — native and ODBC when the target type indicates a warehouse — default to `ReadOnly = true`. Disable explicitly with `OPTIONS(READ_ONLY = FALSE)`.

### Schema cache TTL

Data warehouses can have tens of thousands of tables. The LSP schema cache TTL should default to 5 minutes for warehouse connections (vs. immediate invalidation for OLTP databases) to keep autocomplete responsive without hammering the `INFORMATION_SCHEMA`.

---

## Implementation Checklist

### Phase 1 — Snowflake native connector

- [ ] `ETL-SQL.Core/TokenType.cs`: Add `SNOWFLAKE` keyword.
- [ ] `ETL-SQL.Connectors/SnowflakeConnector.cs` (new): `Snowflake.Data.Client.SnowflakeDbConnection`. Build connection string from `HOST`, `DATABASE`, `SCHEMA`, `WAREHOUSE`, `USERNAME`, `PASSWORD` / `PRIVATE_KEY_FILE` options.
- [ ] Auth: username+password path and private-key JWT path (read PEM, sign JWT, inject as `TOKEN`).
- [ ] `ISchemaProvider` implementation via `INFORMATION_SCHEMA.TABLES` / `COLUMNS`.
- [ ] `DependencyInjectionSetup.cs`: Register `SnowflakeConnector`.
- [ ] Unit tests using Snowflake mock transport.
- [ ] `Category=Integration` tests with free trial account. CI secret: `SNOWFLAKE_CONNECTION_STRING`.
- [ ] `docs/administration/platform/README.md`: Snowflake section.

### Phase 2 — BigQuery native connector

- [ ] `ETL-SQL.Core/TokenType.cs`: Add `BIGQUERY` keyword.
- [ ] `ETL-SQL.Connectors/BigQueryConnector.cs` (new): `Google.Cloud.BigQuery.V2`. Auth via `CREDENTIAL_FILE` (service account JSON) or ADC (no file → `GoogleCredential.GetApplicationDefault()`).
- [ ] Pushdown dialect: register backtick `QuoteIdentifier` impl; handle `project.dataset.table` three-part name resolution.
- [ ] `ISchemaProvider` via `INFORMATION_SCHEMA`.
- [ ] Unit tests against BigQuery emulator Docker image.
- [ ] `Category=Integration` tests using `bigquery-public-data` dataset (no fixture setup). CI secret: `GCP_SA_KEY_JSON`.
- [ ] `docs/administration/platform/README.md`: BigQuery section.

### Phase 3 — Connector interface enhancements (applies to all connectors)

- [ ] `IConnector` / connector metadata: Add `CommandTimeoutSeconds` property. Default: `30` for OLTP, `1800` for warehouse-type connectors.
- [ ] `CREATE CONNECTION OPTIONS(TIMEOUT_SECONDS = n)`: Parse and apply per-connection timeout override.
- [ ] `IConnector`: Add `ReadOnly` flag. Warehouse connectors default `true`.
- [ ] LSP schema cache: Make TTL configurable per connection type; default 5 minutes for warehouse connections.
- [ ] `appsettings.json`: Add `Connectors.DataWarehouse.DefaultCommandTimeoutSeconds`.

### Phase 4 — Documentation and ODBC guidance

- [ ] `docs/administration/platform/README.md`: Add a **Data Warehouse Connections** section that leads with the ODBC path (covers Redshift, Databricks, Synapse, Trino, Dremio) and notes which platforms have native connectors (Snowflake, BigQuery).
- [ ] Include ODBC connection string examples for Redshift, Databricks, Synapse, Trino.
- [ ] `Docs/Architecture/Connectors.md`: Document `CommandTimeoutSeconds` and `ReadOnly` fields.
- [ ] `docs/architecture/standards/Connectors_Standards.md`: Data warehouse connector checklist.

### Phase 5 — DuckDB (low priority — ergonomics, not capability)

- [ ] `ETL-SQL.Core/TokenType.cs`: Add `DUCKDB` keyword.
- [ ] `ETL-SQL.Connectors/DuckDbConnector.cs` (new): `DuckDB.NET.Data`. In-process engine. Passes `READ_PARQUET`, `READ_CSV`, `READ_JSON` table functions through to DuckDB natively. S3/ADLS support via DuckDB extensions (`httpfs`, `azure`).
- [ ] No auth needed for local mode; S3 credentials via `OPTIONS(S3_ACCESS_KEY_ID, S3_SECRET)`.
- [ ] `DependencyInjectionSetup.cs`: Register `DuckDbConnector`.
- [ ] Unit tests: in-process, no `Category=Integration`. Test data: small Parquet file committed to `tests/testdata/`.
- [ ] `docs/administration/platform/README.md`: DuckDB section. Document as "embedded analytical engine" distinct from file connectors.

---

## Revised Effort Estimate

| Phase | Scope | Estimate |
| :--- | :--- | :--- |
| Phase 1 — Snowflake native | New connector, auth, schema introspection | ~2 days |
| Phase 2 — BigQuery native | New connector, auth, dialect handling | ~2 days |
| Phase 3 — Interface enhancements | Timeout, read-only, cache TTL | ~0.5 day |
| Phase 4 — Documentation + ODBC guidance | Docs only | ~0.5 day |
| Phase 5 — DuckDB | New connector, in-process engine | ~1.5 days |
| **Total** | | **~6.5 days** |

Redshift, Databricks, Synapse, Trino, and Dremio require zero connector code — just documentation showing how to use the existing ODBC connector with each platform's driver.

---

## Decision Log

| Question | Decision |
| :--- | :--- |
| Build individual connectors for every platform? | No. ODBC covers Redshift, Databricks, Synapse, Trino, Dremio. Only Snowflake and BigQuery need native connectors. |
| Build a Parquet-specific connector? | Already exists. No new work needed. |
| Build an Avro-specific connector? | Already exists. No new work needed. |
| DuckDB now or later? | Later. It is an ergonomics improvement (in-process SQL over files), not a capability gap. Parquet connector already handles file reading. |
| Tier B (raw Iceberg/Delta on S3)? | Deferred. Apache Iceberg .NET support is immature. Delta Lake .NET is community-maintained. Revisit in 12+ months. |
| Read-only default for warehouse connectors? | Yes. Analytics systems are read-heavy; accidental writes are a real risk. Opt-out with `OPTIONS(READ_ONLY = FALSE)`. |
| Pushdown for BigQuery's QUALIFY clause? | Out of scope for first release. Fall back to local evaluation. |
