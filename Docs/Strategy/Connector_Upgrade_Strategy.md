# ETL-SQL Connector Upgrade Strategy

This document defines the **Roadmap and Implementation Backlog** for modernizing the ETL-SQL connector library. Our goal is 100% technical exhaustion, ensuring every connector is production-grade, pattern-centric, and secure.

---

## 1. Technical Standards Reference

To maintain absolute technical consistency, all implementation work MUST adhere to the permanent architectural and engineering standards:

- **[Connectors Architecture](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors_Architecture.md)**: Details on the `IConnector`/`IDataSource` interfaces, operational **Archetypes** (Expansion vs. Creation), and the **IDatabaseSource** pushdown bridge.
- **[Connectors Engineering Standards](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)**: Mandatory rules for **Credential Masking**, **`ENC:` Support**, and **Pushdown Compliance**.

---

### 2.1 Implemented Connectors (Cloud & Modern)
- **Snowflake Connector** [v0.7.0]
    - **Patterns**: Standard (`ACCOUNT`, `WAREHOUSE`, `DATABASE`, `SCHEMA`, `USER`, `PASS`) vs. OAuth.
    - **Syntax**: `SELECT * FROM snowflake_conn.FactSales;`
- **BigQuery Connector** [v0.7.0]
    - **Patterns**: `PROJECT_ID`, `DATASET_ID`, `KEY_FILE`.
    - **Syntax**: `SELECT * FROM bq_conn.Events;`

## 3. Future Connector Roadmap (Technical Specs)
- **Databricks SQL & Spark**
    - **Pattern**: `HOST`, `HTTP_PATH`, `TOKEN` (Personal Access Token).
- **Delta Sharing**
    - **Pattern**: `PROFILE_PATH` (Local/Cloud path to `.share` JSON profile).
    - **Syntax**: `SELECT * FROM DELTA_SHARING(@Profile) WHERE Share = 'sales';`

### 3.2 Enterprise SaaS (OData/REST)
- **ServiceNow**
    - **Pattern**: `INSTANCE` (slug), `USER`, `PASS` (or `CLIENT_ID`, `CLIENT_SECRET`).
    - **Syntax**: `SELECT * FROM sn.incident WHERE priority = 1;` (OData entities abstracted as SQL tables).
- **Microsoft Dataverse / Dynamics 365**
    - **Pattern**: `ORG_URL`, `TENANT_ID`, `CLIENT_ID`, `CLIENT_SECRET`.
- **SharePoint Connector**
    - **Syntax (List)**: `SELECT * FROM sp.Employees;`
    *   **Syntax (File)**: `SELECT * FROM FLATFILE('doc.csv') ON sp;`

---

## 4. Current Connector Audit (Technical Debt)

The following production-grade options must be added to reach 100% technical exhaustion.

### Relational (MSSQL, Postgres, Oracle)
- [ ] **Failover**: `APPLICATION_INTENT`, `MULTI_SUBNET_FAILOVER`.
- [ ] **Pooling**: `MIN_POOL_SIZE`, `MAX_POOL_SIZE`, `POOL_LIFETIME`.
- [ ] **Security**: `ENCRYPT`, `TRUST_SERVER_CERTIFICATE`, `COLUMN_ENCRYPTION`.
- [ ] **Timeouts**: `COMMAND_TIMEOUT`.

### Flat Files & Documents
- [ ] **Parsing**: `ENCODING`, `CULTURE` (Locale-aware parsing), `TRIM_WHITESPACE`.
- [ ] **Security**: `PASSWORD` for encrypted Workbooks.
- [ ] **Precision**: `FLATTEN_ARRAYS` (JSON/XML).

### Network & Cloud Storage
- [ ] **SFTP**: `HOST_KEY_FINGERPRINT`.
- [ ] **Proxy**: `PROXY_TYPE`, `PROXY_HOST`, `PROXY_PORT`, `PROXY_USER`, `PROXY_PASS`.
- [ ] **AzureBlob**: `RETRY_POLICY`, `MAX_RETRIES`.

---

## 6. Pushdown Optimization Contract

To ensure high-performance execution, SQL-like connectors SHOULD NOT rely solely on row-by-row iteration. They must enable the engine to "Push Down" logic to the target.

### 6.1 The `IDatabaseSource` Interface
Every relational or query-capable connector (Snowflake, Databricks, BigQuery, SQL Server) MUST implement the `IDatabaseSource` interface in its Data Source class.
- **Flag**: Set `SupportsSqlPushdown = true`.
- **Primary Goal**: This informs the engine's optimizer that it can generate a native SQL block and send it to the source instead of extracting raw rows.

### 6.2 Dialect-Aware Translation (The Optimizer Hint)
The engine translates standard ETL-SQL statements (e.g., `INSERT`, `MERGE`, `SELECT`) into native dialects.
- **Contract**: The connector must provide its `DialectProfile` (e.g., `SqlDialect.Postgres`, `SqlDialect.Snowflake`).
- **Effect**: If a user writes `SELECT TOP 10 * FROM x` on a Postgres connection, the engine's pushdown logic will automatically translate it to `SELECT * FROM x LIMIT 10` before sending it to the provider.

### 6.3 Explicit "Pass-Through" Execution
Connectors must handle the `EXECUTE PUSHDOWN` statement.
- **Command**: `ExecuteRawSql(string sql)`.
- **Requirement**: The connector must execute the provided string exactly as received (the "Execute-as-is" pattern). This allows users to utilize target-specific features (e.g., Snowflake's `QUALIFY` or Oracle's `PARTITION` hints) that aren't natively in the ETL-SQL grammar.

### 6.4 Batch & Bulk Acceleration
Where a target supports high-speed bulk ingestion (e.g., Snowflake `COPY INTO` or MSSQL `BCP`), the connector SHOULD intercept standard `INSERT` operations.
- **Auto-Upgrade**: If the engine detects a massive multi-row `INSERT`, the connector's `IDataSource` can internally upgrade this to a bulk-load API call rather than executing individual `INSERT` commands.
- **Lineage**: The connector remains responsible for tracking the count of rows successfully committed during these bulk operations.

---

## 5. Security Hardening Guardrails

Every connector implementation MUST adhere to these "Zero-Trust" security standards.

### 5.1 Credential Masking (The diagnostic "Blackbox")
Connectors are responsible for sanitizing their own metadata when queried for status or diagnostics.
- **Rule**: Properties identified as secrets (`PASS`, `KEY`, `TOKEN`, `SECRET`, `PASSWORD`) MUST be masked with `***` in `GetHelp()`, `GetOptionValues()`, and especially when displayed via `SHOW CONNECTION`.
- **Implementation**: Use the `Common.Security.MaskSecret()` utility when returning configuration maps for display.

### 5.2 Encrypted String Handling
To ensure portable security, connectors must support the `ENC:` prefix for all sensitive fields.
- **Logic**: The `CreateDataSource()` factory method MUST check for the `ENC:` prefix.
- **Workflow**: If a prefix is found, the connector must utilize the `SecurityService` (provided via DI) to decrypt the value before instantiating the underlying driver.
- **Safety**: Plain-text passwords should only be used as a fallback for local development environments.

### 5.3 Non-Bypassable Path Resolution
For connectors that interact with the local filesystem (e.g., `FLATFILE`, `PARQUET`, `SQLITE`, `JSON`), the global sandbox must remain airtight.
- **Enforcement**: All file paths provided in `CREATE CONNECTION` or `WITH(PATH=...)` MUST be passed through the engine's `SecurityService.ResolvePath()` method.
- **Goal**: This prevents "Sandbox Escapes" (e.g., using `../` to access system files) and ensures that script-level operations are confined to the approved workspace.

### 5.4 Secure-by-Default Transport
Connectors must prioritize encrypted transit for all production-grade data sources.
- **SQL Sources**: Default to `ENCRYPT=True` and `TRUST_SERVER_CERTIFICATE=False`.
- **Remote Transfers**: For SFTP/FTP, default to mandatory SSH/TLS.
- **Requirement**: Any "Insecure" override (e.g., forcing no encryption) MUST be explicitly defined by the user and should trigger a `SecurityWarning` in the audit log.

### 5.5 Managed Identity Primacy
Where supported by the target (Azure, AWS, GCP), connectors SHOULD provide a dedicated path for **Passwordless Authentication**.
- **Pattern**: Provide an `AUTHENTICATION` option that accepts `ManagedIdentity` or `ServicePrincipal`, eliminating the need for hardcoded credentials in connection strings.
