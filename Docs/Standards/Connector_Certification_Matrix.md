# Connector Certification Matrix

**ETL-SQL 0.7.x — Last reviewed: 2026-05-23**

This matrix tracks compliance status for every connector against the 10 inviolable rules and the key engineering requirements defined in [Connectors_Standards.md](Connectors_Standards.md). Use it to triage new connector work, prioritize gap remediation, and enforce the certification gate before merging connector changes.

Legend: **✓** Verified · **~** Partial / Needs improvement · **✗** Missing / Not applicable · **N/A** Not required for this connector type

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
| **T2** — Negative path tests | ✓ | ✓ | ~ | ~ | ✓ | ~ |
| **T3** — Credential masking test | ✓ | ✓ | ✓ | ✓ | ✓ | ~ |
| **T4** — Exception wrapping test | ✓ | ✓ | ~ | ~ | ✓ | ~ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ENC: support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **GetExcludedKeywords non-empty** | ✓ | ✓ | ✓ | ~ | ✓ | ✓ |
| **DW: IsDataWarehouse + timeout** | N/A | N/A | N/A | N/A | ✓ | ✓ |
| **DW: TIMEOUT_SECONDS option** | N/A | N/A | N/A | N/A | ✓ | ✓ |
| **DW: ADC / workload identity auth** | N/A | N/A | N/A | N/A | ~ | ✓ |
| **DW: ITransactionalDataSource** | ✓ | ✓ | ✓ | ✓ | ✓ | N/A |
| **Overall** | **✓ GA** | **✓ GA** | **✓ GA** | **~ GA (gaps)** | **✓ GA** | **~ Preview** |

### ODBC Notes
- ODBC wraps arbitrary third-party drivers. Async behavior and exception types depend on the underlying driver. Rule 2 and Rule 5 compliance is best-effort at the ETL-SQL boundary.
- `GetExcludedKeywords()` returns an empty set by design — dialect varies per DSN target. Document this intentional exception.

### BigQuery Notes
- Smoke, masking, and exception-wrapping tests need to be added.
- Workload identity (Application Default Credentials) is implemented but not CI-verified due to lack of a GCP test environment in the pipeline.
- Status: **Preview** — do not enable by default in production deployments without verifying credential configuration.

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
| **T2** — Negative path tests (ResolvePath) | ✓ | ✓ | ✓ | ✓ | ~ | ~ | ✓ |
| **T4** — Exception wrapping test | ✓ | ~ | ✓ | ✓ | ~ | ~ | ✓ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Overall** | **✓ GA** | **~ GA (gaps)** | **✓ GA** | **✓ GA** | **~ GA (gaps)** | **~ GA (gaps)** | **✓ GA** |

### Excel Notes
- `ExcelDataReader` does not expose async read APIs. Rule 2 compliance is documented as an accepted exception; reads are offloaded to `Task.Run` to avoid blocking the async call chain.
- Large multi-sheet files may accumulate rows before yielding. Rule 7 should be verified for workbooks over 100k rows.

### XML Notes
- Refactored to streaming `XmlReader` in 0.7.x: `ReadBatches` performs two lightweight passes (schema discovery + data yield) without loading the full document into memory. Rule 7 compliant.

### Parquet / Avro Notes
- Apache.Parquet and Avro.Net libraries do not provide granular async row-level APIs. Exception wrapping and negative path tests need to be added.

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
| **T1** — Smoke test present | ✓ | ✓ | ~ | ✓ | ~ |
| **T2** — Negative path / credential tests | ✓ | ~ | ~ | ~ | N/A |
| **T3** — Credential masking test | ✓ | ✓ | ✓ | ✓ | ✓ |
| **T4** — Exception wrapping test | ✓ | ~ | ~ | ~ | ~ |
| **Structured WITH() properties** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ENC: support** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **ALTER CONNECTION support** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Overall** | **✓ GA** | **~ GA (gaps)** | **~ GA (gaps)** | **~ GA (gaps)** | **~ GA (gaps)** |

### API Notes
- Streaming is best-effort: paginated API responses are yielded page-by-page (compliant), but `ReadBatches` for non-paginated endpoints buffers the full response body.
- Smoke tests exist for GET/POST flows; PUT/DELETE and auth-scheme tests are missing.

### SMTP Notes
- Write-only connector; no `ReadBatches` path. Rule 7 is N/A.
- Smoke test for actual delivery requires a test SMTP server — currently untested in CI. Use `MailHog` or `Greenmail` as a Testcontainer to add coverage.

### FTP / AZURE_BLOB Notes
- Exception wrapping and negative credential tests need to be added for both.

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
| **T4** — Exception wrapping test | ~ | ~ |
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
| XML DOM accumulates full document (Rule 7) | XML | High | Refactor to streaming `XmlReader` |
| BigQuery CI tests missing | BIGQUERY | High | Add Testcontainers or GCP emulator |
| Exception wrapping tests missing | ORACLE, ODBC, EXCEL, PARQUET, AVRO, FTP, AZURE_BLOB, API, SMTP, REPORTPORTAL, ORCHESTRATOR | Medium | Add T4 tests per connector |
| SMTP CI smoke test missing | SMTP | Medium | Add MailHog Testcontainer |
| AZURE_BLOB negative credential tests | AZURE_BLOB | Medium | Add negative auth tests |
| ODBC GetExcludedKeywords empty by design | ODBC | Low | Document accepted exception in connector source |
| Excel async (accepted exception) | EXCEL | Low | Document Task.Run workaround; track library update |
| Snowflake ADC / JWT auth not CI-verified | SNOWFLAKE | Low | Add CI step with Snowflake test account or emulator |

---

## Using This Matrix

- **Before merging a connector change**: verify all rows for that connector are ✓ or documented ~ (accepted exception with rationale).
- **When adding a new connector**: all applicable rules must be ✓ before the PR can merge. See [Connectors_Standards.md](Connectors_Standards.md) Part I for the full rule text.
- **When a gap becomes a ✓**: update this matrix in the same PR that adds the test or fix.
