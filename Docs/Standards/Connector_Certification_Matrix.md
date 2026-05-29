# Connector Certification Matrix

**ETL-SQL 0.8.0 — Last reviewed: 2026-05-29**

This matrix tracks compliance status for every connector against the 10 inviolable rules and the key engineering requirements defined in [Connectors_Standards.md](Connectors_Standards.md). Use it to triage new connector work, prioritize gap remediation, and enforce the certification gate before merging connector changes.

Legend: **✓** Verified · **~** Partial / Needs improvement · **✗** Missing / Not applicable · **N/A** Not required for this connector type

---

## Certification Coverage Classes

Use these classes when interpreting the matrix and when tagging new connector tests:

| Class | Meaning | Current examples |
| :--- | :--- | :--- |
| Metadata only | Verifies connector registration, supported options, aliases, and dialect declarations without provider I/O. | Connector metadata tests |
| Mocked integration | Exercises connector behavior with mocked provider clients or fake remote file systems. | SFTP constructor/factory tests, provider exception wrapping tests |
| Local real integration | Uses local files, loopback services, or in-process stores with real connector code. | FLATFILE, JSON, XML, PARQUET, AVRO, DIRECTORY, API loopback server, MOCKDB |
| Docker real integration | Uses a disposable container for real protocol/provider compatibility. | SFTP via `atmoz/sftp`, FTP via `delfer/alpine-ftp-server`, SMTP via MailPit, AZURE_BLOB via Azurite, SNOWFLAKE via `ghcr.io/nnnkkk7/snowflake-emulator`, Report Portal and Orchestrator via repository-owned Dockerfiles |
| External/provider real integration | Requires a real cloud or database account outside local CI. | Snowflake cloud auth |

Connector certification tests now carry connector-specific traits such as `Connector=SFTP` and coverage-class traits such as `CertificationClass=DockerRealIntegration`, `CertificationClass=LocalRealIntegration`, `CertificationClass=MockedIntegration`, and `CertificationClass=MetadataOnly`. Use these traits with the existing `Category=Integration` tags to select exact release-gate coverage.

---

## Relational Database Connectors

These connectors implement `IDatabaseSource` with `SupportsSqlPushdown = true`.

| Requirement | MSSQL | POSTGRES | MYSQL | ORACLE | SQLITE | ODBC | SNOWFLAKE | BIGQUERY |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Rule 1** — No SQL evaluation inside connector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 2** — All I/O via async overloads | ✓ | ✓ | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **Rule 3** — Credentials never in logs/exceptions | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 4** — File I/O via ResolvePath | N/A | N/A | N/A | N/A | ✓ | N/A | ~ | ✓ |
| **Rule 5** — Provider exceptions wrapped as ExecutionException | ✓ | ✓ | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **Rule 6** — Sensitive options masked in metadata output | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 7** — O(1) memory streaming via ReadBatches | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 8** — Implements IDatabaseSource + pushdown | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 9** — GetExcludedKeywords declared | ✓ | ✓ | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **Rule 10** — DisposeAsync releases all resources | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T1** — Smoke test present | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T2** — Negative path tests | ✓ | ✓ | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **T3** — Credential masking test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T4** — Exception wrapping test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ENC: support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **GetExcludedKeywords non-empty** | ✓ | ✓ | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **DW: IsDataWarehouse + timeout** | N/A | N/A | N/A | N/A | N/A | N/A | ✓ | ✓ |
| **DW: TIMEOUT_SECONDS option** | N/A | N/A | N/A | N/A | ✓ | N/A | ✓ | ✓ |
| **DW: ADC / workload identity auth** | N/A | N/A | N/A | N/A | N/A | N/A | ~ | ✓ |
| **DW: ITransactionalDataSource** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | N/A |
| **Overall** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** | **~ GA (gaps)** | **✓ GA** | **✓ GA** |

### ODBC Notes
- ODBC wraps arbitrary third-party drivers. Async behavior and exception types depend on the underlying driver. Rule 2 and Rule 5 compliance is best-effort at the ETL-SQL boundary.
- `GetExcludedKeywords()` returns an empty set by design — dialect varies per DSN target. Document this intentional exception.

### Snowflake Notes
- Docker-backed emulator coverage uses the MIT-licensed `ghcr.io/nnnkkk7/snowflake-emulator` image for local official-driver connectivity, Snowflake SQL function execution, and sanitized failure wrapping.
- The emulator does not validate Snowflake cloud authentication, authorization, warehouse behavior, or all DDL/DML result metadata. Full provider auth remains an external sign-off item.

### BigQuery Notes
- **T2/T3/T4** covered by `BigQueryConnectorUnitTests` (no Docker required): host allowlist enforcement, credential masking, invalid credential wrapping.
- **T1** covered by `BigQueryIntegrationTests` (`Category=Integration`) using Testcontainers `ghcr.io/goccy/bigquery-emulator`. Requires Docker at runtime.
- Workload identity (Application Default Credentials) is implemented but not CI-verified due to lack of a GCP test environment in the pipeline.
- Status: **GA** — all certification tiers covered; T1 smoke test requires Docker in CI.

### MySQL Notes
- **T1** covered by `MySqlTests` (`Category=Integration`) using Testcontainers `mysql:8.0`. Requires Docker at runtime.
- **T2/T3/T4** covered by `ConnectorMetadataTests` (no Docker required) asserting credential masking, exception wrapping, and option parsing.
- Status: **GA** — all certification tiers covered; T1 smoke test requires Docker in CI.

---

## File Connectors

These connectors do not implement `IDatabaseSource`. All SQL is evaluated by the ETL-SQL engine, not pushed to the data source.

| Requirement | FLATFILE | EXCEL | JSON | XML | PARQUET | AVRO | DIRECTORY |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Rule 1** — No SQL evaluation inside connector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 2** — All I/O via async overloads | ✓ | ~ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 3** — Credentials never in logs/exceptions | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 4** — File I/O via ResolvePath | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 5** — Provider exceptions wrapped as ExecutionException | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 6** — Sensitive options masked | N/A | N/A | N/A | N/A | N/A | N/A | N/A |
| **Rule 7** — O(1) memory streaming via ReadBatches | ✓ | ~ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 8** — IDatabaseSource + pushdown | N/A | N/A | N/A | N/A | N/A | N/A | N/A |
| **Rule 9** — GetExcludedKeywords declared | N/A | N/A | N/A | N/A | N/A | N/A | N/A |
| **Rule 10** — DisposeAsync releases all resources | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T1** — Smoke test present | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T2** — Negative path tests (ResolvePath) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T4** — Exception wrapping test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Overall** | **✓ GA** | **~ GA (gaps)** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** |

### Excel Notes
- `ExcelDataReader` does not expose async read APIs. Rule 2 compliance is documented as an accepted exception; reads are offloaded to `Task.Run` to avoid blocking the async call chain.
- Large multi-sheet files may accumulate rows before yielding. Rule 7 should be verified for workbooks over 100k rows.

### XML Notes
- Refactored to streaming `XmlReader` in 0.7.x: `ReadBatches` performs two lightweight passes (schema discovery + data yield) without loading the full document into memory. Rule 7 compliant.

### Parquet / Avro Notes
- Apache.Parquet and Avro.Net libraries do not provide granular async row-level APIs. Exception wrapping tests added (T4 ✓), and corrupt-file negative-path reads are covered for both connectors (T2 ✓).

---

## Remote / Network Connectors

| Requirement | SFTP | FTP | AZURE_BLOB | S3 | API | SMTP | SHAREPOINT | AD | MONGODB | KAFKA |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Rule 1** — No SQL evaluation inside connector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 2** — All I/O via async overloads | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ~ | ✓ | ~ |
| **Rule 3** — Credentials never in logs/exceptions | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 4** — File I/O via ResolvePath (local staging) | ✓ | ✓ | ✓ | ✓ | N/A | N/A | ✓ | N/A | N/A | N/A |
| **Rule 5** — Provider exceptions wrapped as ExecutionException | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 6** — Sensitive options masked | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 7** — O(1) memory streaming | ✓ | ✓ | ✓ | ✓ | ~ | N/A | ✓ | ✓ | ✓ | ✓ |
| **Rule 8** — IDatabaseSource + pushdown | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A |
| **Rule 9** — GetExcludedKeywords | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A |
| **Rule 10** — DisposeAsync releases all resources | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T1** — Smoke test present | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T2** — Negative path / credential tests | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T3** — Credential masking test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T4** — Exception wrapping test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ENC: support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Overall** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** | **~ GA (gaps)** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** | **✓ GA** |

### API Notes
- Streaming is best-effort: paginated API responses are yielded page-by-page (compliant), but `ReadBatches` for non-paginated endpoints buffers the full response body.
- Local loopback HTTP tests cover GET/POST smoke paths, PUT/DELETE methods, and Basic, Bearer, and API key authentication headers.

### SMTP Notes
- Write-only connector; no `ReadBatches` path. Rule 7 is N/A.
- Docker-backed MailPit smoke coverage verifies successful delivery, multi-row send, connection-refused wrapping, host allowlist denial, and credential masking.

### FTP / AZURE_BLOB Notes
- Exception wrapping tests added (T4 ✓).
- FTP has Docker-backed coverage for mapped-port connection setup, `PORT` option handling through `CreateDataSource`, root listing, upload/download round trip, and wrong-password provider failure wrapping.
- AZURE_BLOB has Azurite-backed smoke, upload/list/download, bad account key, expired SAS token, blocked host, and connection-string host parsing coverage.

### SharePoint Notes
- Implements both remote filesystem operations (`IRemoteFileSystem`) and tabular queries (`IDataSource` against lists).
- Verified via mock auth/OData payload unit tests and real `SharePointIntegrationTests` utilizing a local HTTP loopback server to verify active REST connections, list retrieval, and file synchronization.

### Active Directory / LDAP Notes
- `LdapConnection` synchronous request send-action behaves under a partial Rule 2 rating (`~`). Re-wraps LDAP exception codes to standardized engine `ExecutionException`.
- Verified via mock unit tests and real `ActiveDirectoryIntegrationTests` / `PortalLdapIntegrationTests` using a Docker-backed OpenLDAP container to verify active directory metadata retrieval, filter context translation, user auto-provisioning, and role-mapping sync.

### SQLite Notes
- Uses local file or memory databases via Microsoft.Data.Sqlite. Rule 4 (ResolvePath) is applied on the Database File path context at construction time to respect the security boundaries.
- Dialect exclusions such as `TOP` and pushdown keywords (`LIMIT`, `OFFSET`) are fully declared.
- Verified via `SqliteAndS3ConnectorTests` asserting in-memory and temp-file table execution, data batch streams, and transactional rollback/commit.

### S3 Compatible Notes
- AWS S3 SDK (AWSSDK.S3) handles custom HTTP endpoints, region configurations, and path-style addressing.
- Egress policies are validated against parsed endpoints before resolving client handlers.
- Verified via mock transport tests and real `S3IntegrationTests` utilizing a Docker-backed MinIO container to verify connection status validation, bucket listing, uploads, downloads, and lifecycle operations.

### MongoDB Notes
- Flattens nested BSON documents and arrays into valid JSON strings using ToJson() to preserve complex data hierarchies.
- Schema discovery queries the first document in the collection dynamically; subsequent rows map to these discovered columns.
- Verified via mock unit tests and real `MongodbIntegrationTests` using a Docker-backed MongoDB container to verify active connection validation (ping/buildInfo) and collection queries.

### Kafka Notes
- Rule 2 async-poll rating is partial (`~`) because the underlying Confluent.Kafka consumer only exposes a synchronous `Consume(TimeSpan)` loop rather than async overloads.
- Batch-bounded reads are supported through `TIMEOUT_MS` and `MAX_MESSAGES` controls to prevent infinite stream blocks.
- Writes translate each data row into a JSON message published to the broker.
- Verified via mock unit tests and real `KafkaIntegrationTests` using a Docker-backed Redpanda container to verify active connection checks, and message production/consumption loops.

---

## Platform Connectors

| Requirement | REPORTPORTAL | ORCHESTRATOR |
| :--- | :---: | :---: |
| **Rule 1** — No SQL evaluation inside connector | ✓ | ✓ |
| **Rule 2** — All I/O via async overloads | ✓ | ✓ |
| **Rule 3** — Credentials never in logs/exceptions | ✓ | ✓ |
| **Rule 4** — File I/O via ResolvePath | N/A | N/A |
| **Rule 5** — Provider exceptions wrapped as ExecutionException | ✓ | ✓ |
| **Rule 6** — Sensitive options masked (API key) | ✓ | N/A |
| **Rule 7** — O(1) memory streaming | N/A | N/A |
| **Rule 8** — IDatabaseSource + pushdown | N/A | N/A |
| **Rule 9** — GetExcludedKeywords | N/A | N/A |
| **Rule 10** — DisposeAsync releases all resources | ✓ | ✓ |
| **T1** — Smoke test present | ✓ | ✓ |
| **T3** — Credential masking test | ✓ | N/A |
| **T4** — Exception wrapping test | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ |
| **ENC: support** | ✓ | N/A |
| **ALTER CONNECTION support** | ✓ | N/A |
| **Overall** | **✓ GA** | **✓ GA** |

### Platform Connector Notes
- Docker-backed smoke coverage uses repository-owned Report Portal and Orchestrator Service images built from `src/ETL-SQL.ReportPortal/Dockerfile` and `src/ETL-SQL.Orchestrator.Service/Dockerfile`.
- Build the images before running the platform smoke tests:
  - `docker build -f src/ETL-SQL.ReportPortal/Dockerfile -t etl-sql-reportportal-test:latest .`
  - `docker build -f src/ETL-SQL.Orchestrator.Service/Dockerfile -t etl-sql-orchestrator-service-test:latest .`
- Report Portal smoke coverage verifies connector authentication against a real containerized portal and executes `SHOW PORTAL USERS`.
- Orchestrator smoke coverage verifies API-key authentication against a real containerized orchestrator and executes create/list scheduled-job operations through the connector.

---

## Test / Internal Connectors

| Requirement | MOCKDB |
| :--- | :---: |
| **Rule 1** — No SQL evaluation | ✓ |
| **Rule 2** — Async I/O | ✓ |
| **Rule 3** — Credentials out of logs | N/A |
| **Rule 5** — Exceptions wrapped | ✓ |
| **Rule 7** — O(1) streaming | ✓ |
| **Rule 8** — IDatabaseSource | ✓ |
| **T1** — Smoke test | ✓ |
| **Overall** | **✓ Internal use only** |

---

## Open Gaps Summary

Sorted by estimated risk:

| Gap | Connectors Affected | Risk | Action |
| :--- | :--- | :---: | :--- |
| Snowflake full provider auth not CI-verified | SNOWFLAKE | Low | Add CI step with a real Snowflake test account; emulator coverage is local driver/query smoke only |

---

## Using This Matrix

- **Before merging a connector change**: verify all rows for that connector are ✓ or documented ~ (accepted exception with rationale).
- **When adding a new connector**: all applicable rules must be ✓ before the PR can merge. See [Connectors_Standards.md](Connectors_Standards.md) Part I for the full rule text.
- **When a gap becomes a ✓**: update this matrix in the same PR that adds the test or fix.
