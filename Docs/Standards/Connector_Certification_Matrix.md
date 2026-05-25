# Connector Certification Matrix

**ETL-SQL 0.7.x — Last reviewed: 2026-05-25**

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
| Docker real integration | Uses a disposable container for real protocol/provider compatibility. | SFTP via `atmoz/sftp`, FTP via `delfer/alpine-ftp-server`, SMTP via MailPit, AZURE_BLOB via Azurite, SNOWFLAKE via `ghcr.io/nnnkkk7/snowflake-emulator` |
| External/provider real integration | Requires a real cloud or database account outside local CI. | Snowflake cloud auth and BigQuery production sign-off |

Connector certification tests now carry connector-specific traits such as `Connector=SFTP` and coverage-class traits such as `CertificationClass=DockerRealIntegration`, `CertificationClass=LocalRealIntegration`, `CertificationClass=MockedIntegration`, and `CertificationClass=MetadataOnly`. Use these traits with the existing `Category=Integration` tags to select exact release-gate coverage.

---

## Relational Database Connectors

These connectors implement `IDatabaseSource` with `SupportsSqlPushdown = true`.

| Requirement | MSSQL | POSTGRES | ORACLE | ODBC | SNOWFLAKE | BIGQUERY |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Rule 1** — No SQL evaluation inside connector | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 2** — All I/O via async overloads | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **Rule 3** — Credentials never in logs/exceptions | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 4** — File I/O via ResolvePath | N/A | N/A | N/A | N/A | ~ | ✓ |
| **Rule 5** — Provider exceptions wrapped as ExecutionException | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **Rule 6** — Sensitive options masked in metadata output | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 7** — O(1) memory streaming via ReadBatches | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 8** — Implements IDatabaseSource + pushdown | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 9** — GetExcludedKeywords declared | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **Rule 10** — DisposeAsync releases all resources | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T1** — Smoke test present | ✓ | ✓ | ✓ | ✓ | ✓ | ~ |
| **T2** — Negative path tests | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **T3** — Credential masking test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T4** — Exception wrapping test | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ENC: support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **GetExcludedKeywords non-empty** | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **DW: IsDataWarehouse + timeout** | N/A | N/A | N/A | N/A | ✓ | ✓ |
| **DW: TIMEOUT_SECONDS option** | N/A | N/A | N/A | N/A | ✓ | ✓ |
| **DW: ADC / workload identity auth** | N/A | N/A | N/A | N/A | ~ | ✓ |
| **DW: ITransactionalDataSource** | ✓ | ✓ | ✓ | ✓ | ✓ | N/A |
| **Overall** | **✓ GA** | **✓ GA** | **✓ GA** | **~ GA (gaps)** | **✓ GA** | **~ GA (T1 needs Docker)** |

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

| Requirement | SFTP | FTP | AZURE_BLOB | API | SMTP |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Rule 1** — No SQL evaluation inside connector | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 2** — All I/O via async overloads | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 3** — Credentials never in logs/exceptions | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 4** — File I/O via ResolvePath (local staging) | ✓ | ✓ | ✓ | N/A | N/A |
| **Rule 5** — Provider exceptions wrapped as ExecutionException | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 6** — Sensitive options masked | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Rule 7** — O(1) memory streaming | ✓ | ✓ | ✓ | ~ | N/A |
| **Rule 8** — IDatabaseSource + pushdown | N/A | N/A | N/A | N/A | N/A |
| **Rule 9** — GetExcludedKeywords | N/A | N/A | N/A | N/A | N/A |
| **Rule 10** — DisposeAsync releases all resources | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T1** — Smoke test present | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T2** — Negative path / credential tests | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T3** — Credential masking test | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T4** — Exception wrapping test | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ENC: support** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Overall** | **✓ GA** | **✓ GA** | **✓ GA** | **~ GA (gaps)** | **✓ GA** |

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
| **T1** — Smoke test present | ~ | ~ |
| **T3** — Credential masking test | ✓ | N/A |
| **T4** — Exception wrapping test | ✓ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ |
| **ENC: support** | ✓ | N/A |
| **ALTER CONNECTION support** | ✓ | N/A |
| **Overall** | **~ GA (tests sparse)** | **~ GA (tests sparse)** |

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
