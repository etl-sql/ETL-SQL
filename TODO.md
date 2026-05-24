# ETL-SQL Development
## Additions
- [x] **SET SHOW_SECRETS**  It was thought that SET SHOW_SECRETS is a better naming over SET SHOW_PASSWORDS can we just make them aliases of each other but we'll use SHOW_SECRETS as the preferred.

- [x] **Expand `etl-sql doctor` into a real install validation command — phase 1**
  - Current state: `etl-sql doctor` already exists in `src/ETL-SQL.App/App/EngineRunner.cs` and is wired in `CliOrchestrator.cs`.
  - Current checks are shallow: OS, .NET runtime, write access to `AppDomain.CurrentDomain.BaseDirectory`, hard-coded ODBC result, and `Security:AuthorizedHosts` count.
  - Improve the command instead of adding a new one.
  - Add checks for:
    - App config load and effective paths: logs, sessions, spill, safe zones, script roots, report snapshot/data roots.
    - Write access to actual runtime paths, not only the app base directory.
    - Parser/lexer smoke script.
    - Engine execution smoke script against `MOCKDB` or `DUAL`.
    - Linter smoke result.
    - Security guardrail smoke: blocked system path, blocked script-file read, secret redaction sample.
    - `ENC:` encrypt/decrypt round trip using a temporary password.
    - File IO smoke inside an approved temp/safe path.
    - Report build smoke using a tiny inline `.rptsql` or embedded sample.
    - Shared report runtime asset drift check equivalent to `node .\scripts\sync-assets.js -Check` when running from source.
    - Optional dependency checks: Node, VS Code extension build assets, Graphviz/browser/PDF support if required by installed features.
    - Optional service checks: Report Portal `/health`, Orchestrator `/health`, configured SMTP/SFTP/Blob endpoints.
  - Add output modes:
    - Human table output.
    - `--json` for automation.
    - `--strict` returns non-zero on warnings.
    - `--profile quick|full` so first-run checks stay fast but release validation can go deeper.
  - Document it in README, User_Manual, Administrators_Guide, and Syntax/CLI reference docs.
  - Status review 2026-05-24: quick/full profiles, human/JSON output, strict mode, runtime-path write checks, parser/engine/linter/security/encryption/file/report-parser/asset/Node/portal-DB checks are implemented and documented.

- [ ] **`etl-sql doctor` remaining full-profile checks**
  - Add a real report build smoke, not only Report-SQL parser smoke.
  - Add optional Graphviz/browser/PDF capability checks when installed features require them.
  - Add optional service checks for Report Portal `/health`, Orchestrator `/health`, and configured SMTP/SFTP/Blob endpoints.
  - Add explicit tests for `doctor --json`, `doctor --strict`, and `doctor --profile quick|full` exit-code behavior.

- [x] **Connector certification matrix — phase 1**
  - Goal: prove which connectors are production-tested versus syntax/plumbing-tested.
  - Create a document and test tags that classify each connector as:
    - Metadata only.
    - Mocked integration.
    - Local real integration.
    - Docker real integration.
    - External/provider real integration.
  - For each connector verify:
    - `CREATE CONNECTION` syntax and aliases.
    - `ENC:`/sensitive option handling.
    - Host/path guardrails where applicable.
    - `ReadBatches()` honors batch size and does not materialize full source.
    - `WriteBatches()` behavior or explicit unsupported behavior.
    - Provider exception wrapping/sanitization.
    - What-if/no-write behavior where applicable.
    - Schema/table/column discovery.
    - Pushdown behavior for SQL connectors.
  - Add a generated status table to docs so users know what has been verified.
  - Status review 2026-05-24: matrix exists and has been refreshed for XML streaming, SMTP Docker, and AZURE_BLOB Azurite coverage. Remaining gap: add connector-specific test traits/classifications (`Connector=...`, `CertificationClass=...`) so release gates can select metadata-only, mocked, local-real, Docker-real, and external-provider coverage precisely.

- [x] **Real SFTP integration test lane**
  - Current state: SFTP connector exists and tests verify constructor/plumbing with factories and mock remote file systems.
  - Missing: live SFTP server compatibility test.
  - Add Docker-based SFTP service for integration tests.
  - Cover:
    - Password authentication.
    - Private-key authentication.
    - Private-key plus passphrase.
    - `REMOTE_FILE_LIST`.
    - `SEND FILE` upload.
    - `RECEIVE FILE` download.
    - Delete remote file if supported/available through current syntax.
    - `OVERWRITE=ON|OFF`.
    - Missing remote path.
    - Permission denied.
    - Host not in authorized-host allowlist.
    - Large file transfer with checksum verification.
    - Provider exception messages do not leak credentials.
  - Add a CI/manual lane distinction:
    - Fast mocked tests stay in normal CI.
    - Docker SFTP lane runs in integration/release validation.
  - Status review 2026-05-24: added missing permission-denied coverage with `UploadToUnauthorizedRootPath_ThrowsExecutionException`.

- [x] **Scale certification suite for batching/spilling claims — smoke harness**
  - Important framing: this is not "build large-data support from scratch." A lot already exists:
    - `ReadBatches()`/`WriteBatches()` contracts.
    - `BatchSize`, `MaxInMemoryBatches`, join/window/temp spill thresholds.
    - `SpillStore` with encrypted/compressed spill IO.
    - External sort, join, aggregate, grouping-set, and window spill paths.
    - In-memory temp-table spill tests.
    - Performance/hardening tests under `tests/ETL-SQL.PerfTests`, `tests/ETL-SQL.Tests/Hardening/Performance`, and `tests/ETL-SQL.Tests/Operations`.
    - Spill telemetry such as `TotalSpilledBytes` and profile output.
  - The gap is not implementation volume; the gap is certification:
    - We need a user/release-facing answer to "which large-data shapes are certified, at what size, on what machine profile, and with what memory bound?"
    - Current tests prove many individual mechanisms, but they do not yet produce a single matrix of product claims.
  - Build this as a certification harness layered on top of existing tests:
    - Reuse existing hardening/perf tests wherever possible.
    - Tag certification tests separately, for example `Category=ScaleCertification`, so they do not slow normal CI.
    - Add `scripts/Test-ScaleCertification.ps1` to run the lane and write a machine-readable result artifact.
    - Emit a summary JSON/Markdown table with scenario, row count, data width, elapsed time, peak working set, managed memory delta, spill bytes, cleanup result, and pass/fail.
    - Keep thresholds configurable by environment so developer laptops and release agents can use different row counts.
  - Define certification tiers instead of one vague "any size" claim:
    - `Smoke`: 50k-100k rows, runs quickly on PR/local.
    - `Standard`: 1M+ rows, release validation.
    - `Stress`: 10M+ rows or larger files, manual/nightly only.
    - `Provider`: real connector-backed tests where available, not only in-memory sources.
  - Certified scenarios should include:
    - Large generated source into `#temp` with temp-table spill forced low.
    - Large CSV ingest with `SELECT INTO #temp`.
    - Large Parquet read/write round trip.
    - Large `SELECT *` streaming path with bounded display/result retention.
    - Large `ORDER BY` forcing external sort.
    - Large equality join forcing external join.
    - Large group by forcing external aggregate.
    - Large grouping sets/cube spill path.
    - Large window function forcing window/deep-spill path.
    - Large scalar subquery/cache path where applicable.
    - Large report `CREATE DATASET` snapshot and reload from Parquet cache.
    - Large `MERGE`, `UPDATE`, and `DELETE` review: certify if bounded, otherwise document limits and add warnings.
  - Each scenario should assert more than "does not crash":
    - Correct row count.
    - Stable checksum or aggregate total.
    - `TotalSpilledBytes` increases when the scenario is supposed to spill.
    - Peak memory stays under a documented bound for the tier.
    - Last-result/display row caps prevent UI/result blowup.
    - Spill/session/temp files are cleaned up after success and after forced failure.
    - Logs/profile output contain enough evidence for a user to understand the chosen execution path.
  - Add an explicit operator audit:
    - List every statement/operator that streams, spills, partially materializes, or fully materializes.
    - For fully materializing paths, decide whether to improve them or document practical size limits.
    - Pay special attention to `MERGE`, `UPDATE`, `DELETE`, report dataset builds, portal snapshots, and connector implementations that may ignore batch size.
  - Documentation deliverables:
    - Add a "Large Data Certification" page with the current matrix and machine profile.
    - Update user-facing wording from vague "any size data" to precise claims such as "certified streaming/spill paths for these operators and row counts."
    - Link failed/uncertified paths to TODOs or documented limits.
  - Status review 2026-05-24: smoke-tier harness exists for sort, aggregate, join, temp spill, result cap, and window. Row scaling now flows from `CERT_ROW_SCALE`, and spill-path scenarios assert `TotalSpilledBytes > 0`. This is not full large-data product certification yet.

- [ ] **Scale certification suite — remaining coverage**
  - Fix and certify report `CREATE DATASET` snapshot/reload beyond one 10k-row batch.
  - Add grouping sets/cube spill path and scalar subquery/cache path.
  - Add provider-backed large-data certification where real connectors are available.
  - Add cleanup assertions for spill/session/temp files after success and forced failure.
  - Add memory-bound assertions per tier.
  - Certify or explicitly warn on `MERGE`, `UPDATE`, and `DELETE` boundedness.
  - Add true `Standard`/`Stress` test traits instead of only scaling the Smoke scenarios.

- [ ] **Report dataset metadata row count certification**
  - `Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum` is present but skipped because `CREATE DATASET` Parquet snapshot/reload currently returns only the first 10k-row batch for a 50k smoke dataset.
  - Audit the `CREATE DATASET` write/read path and `Telemetry.LastStatementRowsProcessed` for batched `SELECT INTO`/`CREATE DATASET`; both cached row content and `DatasetMetadata.RowCount` should reflect the full materialized row count.

- [x] **Persistent lineage and stewardship catalog — core history**
  - Added `ILineageCatalogStore` interface with `SaveLineageAsync`, `GetHistoryForTableAsync`, `GetHistoryForTagAsync` to `ETL-SQL.Core/Data/`.
  - `SQLiteJobHistoryStore` now implements `ILineageCatalogStore`, persisting entries to a `LineageHistory` table in `etlsql.db`. Schema migrates automatically via `CREATE TABLE IF NOT EXISTS` on startup.
  - `ScriptExecutorAdapter` injects `ILineageCatalogStore` and persists all lineage entries after each orchestrated job run (with `jobName`). Ad-hoc runs store `JobName = null`.
  - `SchedulerService` passes `job.Name` to `executor.ExecuteTextAsync` via new optional `jobName` parameter on `IScriptExecutor`.
  - New AST nodes: `ShowLineageHistoryForTableStatement`, `ShowLineageHistoryForTagStatement`.
  - Parser: `SHOW LINEAGE HISTORY FOR TABLE <name> [LIMIT n] [INTO #t]` and `SHOW LINEAGE HISTORY FOR TAG <key> [= 'value'] [LIMIT n] [INTO #t]`.
  - New handlers: `ShowLineageHistoryForTableStatementHandler`, `ShowLineageHistoryForTagStatementHandler` (auto-discovered via DI).
  - 16 tests in `LineageCatalogTests.cs` covering save/query/tag-filter/limit/null-job/empty/idempotent-init and all parser forms.
  - Remaining work split below: portal/report views for lineage; `SHOW REPORT DEPENDENCIES` lineage enrichment; cross-run lineage for `CREATE DATASET`/`CREATE VISUAL`/published bundles.

- [ ] **Persistent lineage and stewardship catalog — report/portal integration**
  - Add portal/report views for lineage and tags, not just ETL output tables.
  - Enrich `SHOW REPORT DEPENDENCIES` with lineage/tag context where available.
  - Add cross-run lineage continuity for `CREATE DATASET`, `CREATE VISUAL`, published bundles, and scheduled report refresh jobs.
  - Add tests answering stewardship questions:
    - What scripts write to this table?
    - What reports use this dataset/table/column?
    - What jobs touched PII-tagged columns this week?
    - Which outputs were derived from a given source file?

- [x] **Documentation truth and findability audit — phase 1**
  - Fixed stale "Phase 7 (view transparency)" backlog reference in `Docs/Reference/Lineage.md`; replaced with current engine behaviour note.
  - Confirmed `Docs/README.md` landing map covers all 6 user-goal entries (SSIS/ETL, reports, scheduling, source control, lineage, troubleshooting).
  - Confirmed `Docs/Strategy/README.md` classifies all strategy docs as roadmap/non-reference.
  - Added `tests/ETL-SQL.Tests/Docs/DocSanityTests.cs` with three tests (all pass):
    - `SampleFiles_ReferencedInSampleGuide_AllExist` — every `../samples/…` link in `Sample_Guide.md` exists on disk.
    - `Grammar_SqlBlocks_ParseWithoutSyntaxError` — every non-placeholder `sql` block in `Grammar.md` (138 total) parses without `SyntaxException`/`ParseException`.
    - `HelpFiles_AllNonEmpty` — every `.md` file under `Resources/Help/` is non-empty.

- [ ] **Documentation truth and findability audit — remaining checks**
  - Parse SQL blocks in `Syntax_Index.md` and help files, not only `Grammar.md`.
  - Verify every documented keyword has a help entry or an explicit no-help exception.
  - Verify every help entry links back to the canonical reference page.
  - Add a generated report of stale roadmap/backlog language that appears in reference docs.

- [x] **Report Portal operational hardening review — API/core**
  - Added `EXPORT_CSV` and `EXPORT_PDF` audit events to `ExportController` (previously unlogged).
  - Added three tests to `PortalIntegrationTests.cs` (all passing; total portal coverage now 55 tests):
    - `Snapshot_FailedRefresh_KeepsLastGoodSnapshot` — verifies old snapshot is still served after a failed re-execute, and catalog lists `Failed` status with `snapshotBuiltAt` still populated.
    - `AuditLog_RecordsViewSnapshotExportAndSubscriptionEvents` — verifies `VIEW_SNAPSHOT`, `EXPORT_CSV`, `CREATE_SUBSCRIPTION`, and `DELETE_SUBSCRIPTION` all appear in the audit log after being triggered via API.
    - `Subscription_WithParameters_PersistsAndRoundTrips` — verifies per-subscription parameter overrides survive a PUT update round-trip.
  - Confirmed: orchestrator unavailable → health check returns `Degraded` (OrchestratorHealthCheck already wired; tested via health smoke test).
  - Confirmed: production readiness checklist already exists in `Docs/ReportPortal_Administrators_Guide.md` §14.
  - Remaining work split below: report history error surfaces and horizontal scrolling.

- [ ] **Report Portal operational hardening review — remaining UI/concurrency**
  - Test concurrent refresh/view behavior.
  - Add UI or browser verification that report history/error surfaces remain readable without horizontal scrolling where possible.
  - Add permission edge-case tests across combined folder/report/dataset/export access where gaps remain.

- [ ] **Connector certification gap remediation** *(see `Docs/Standards/Connector_Certification_Matrix.md` for full detail)*

  **High risk**
  - [x] **XML streaming refactor** — XML connector accumulates the full document in a DOM before yielding rows (Rule 7 violation). Refactor to streaming `XmlReader` so large XML files do not materialize fully in memory.
  - [x] **BigQuery CI tests** — `BigQueryConnectorUnitTests` (no Docker) covers T4 exception wrapping (invalid JSON creds, missing file), T3 credential masking (private key material not leaked), and T2 host allowlist enforcement. `BigQueryIntegrationTests` (`Category=Integration`) covers T1 smoke tests via Testcontainers `ghcr.io/goccy/bigquery-emulator`. All 4 unit tests pass; T1 requires Docker at runtime.

  **Medium risk**
  - [x] **Exception wrapping tests (T4) — 11 connectors** — T4 tests added for ORACLE, ODBC, EXCEL, PARQUET, AVRO, FTP, AZURE_BLOB, API, SMTP, REPORTPORTAL, and ORCHESTRATOR in `ConnectorExceptionWrappingTests.cs`. All 11 pass.
  - [x] **SMTP Docker smoke test** — `SmtpFixture.cs` starts an `axllent/mailpit:latest` container; `SmtpIntegrationTests.cs` covers successful send+verify via MailPit API, multi-row batch, connection refused → ExecutionException, host not in allowlist → SecurityException, and credential masking.
  - [x] **AZURE_BLOB negative credential and path tests** — `AzureBlobFixture.cs` starts Azurite; `AzureBlobIntegrationTests.cs` covers: smoke (valid creds, empty container), upload+list round-trip, download, bad account key → ExecutionException on ReadBatches and Upload, host not in allowlist → SecurityException. `AzureBlobConnectorUnitTests` covers GetHostStatic parsing for all connection string formats.

  **Low risk / documentation**
  - [x] **ODBC — document GetExcludedKeywords accepted exception** — Explicit override with comment added to `OdbcConnector.cs`.
  - [x] **Excel — document async accepted exception** — Comment added to `ExcelDataSource.cs` at the `AsDataSet` call.
  - [x] **Snowflake ADC/JWT auth — CI verification** — Fixed a recursive `SnowflakeDataSource.CreateCommand(conn)` StackOverflow bug (was calling itself instead of `conn.CreateCommand()`). Added `SnowflakeDataSourceTests`: host allowlist enforcement (SecurityException), JWT connection string authenticator properties, host suffix normalisation logic. Full production sign-off still requires a real Snowflake account (see doc comment in `SnowflakeConnectorTests.cs` for manual steps).

## Bugs
### VS Code
- [x] **Password not working**  In VS Code I added a password to encrypt a connection.  When I reopened the file and ran the script vs code asked for the password.  I put it in and it gave me this error: ETL-SQL password: requires an interactive console.
- [x] **Test coverage slipped below 70%** — Back to 70.8% after T4 exception wrapping tests were added.
### General
- [x] **Is SLT corpus complete** — Audited and documented. Short answer: the corpus is intentionally SELECT-focused and appropriate for ETL-SQL; the runner had a critical bug that made it run zero files.
  - **Bug fixed**: `SltTests.GetTestFiles()` filtered to "slt_good_10.test" (a non-existent file), so `RunAllSltTests` was always passing trivially with 0 tests. Fixed to include all non-empty .test files except explicit exclusions.
  - **Upstream source**: SQLite Logic Test suite (D. Richard Hipp). We carry `corpus/select1-5.test` (the 5 most comprehensive SELECT-coverage files from the original SLT). The full suite has 622 `slt_good_N.test` files; those beyond select1-5 cover triggers, REINDEX, and other SQLite-specific features ETL-SQL doesn't support.
  - **Included** (effective after bug fix):
    - `corpus/select1.test` — 1,031 records (31 stmt + 1,000 query)
    - `corpus/select2.test` — 1,031 records (NULLs in data)
    - `corpus/select3.test` — 3,351 records (31 stmt + 3,320 query)
    - `corpus/select4.test` — 3,857 records (1,025 stmt + 2,832 query; large, OOM risk)
    - `corpus/select5.test` — 1,436 records (704 stmt + 732 query; large)
    - `evidence/in1.test`, `in2.test` — IN-predicate tests (216 + 54 records)
    - `evidence/slt_lang_createview.test`, `dropview.test`, `droptable.test`, `dropindex.test`, `reindex.test`, `replace.test`, `update.test`
    - All 23 root ETL-SQL custom tests (aggregates, cte, window, join, subquery, etc.)
  - **Excluded** (with reasons):
    - `corpus/select4_debug.test` — debug variant, overlaps entirely with `select4.test`
    - `evidence/slt_lang_createtrigger.test`, `slt_lang_droptrigger.test` — ETL-SQL has no trigger support
    - `evidence/slt_lang_aggfunc.test` — uses SQLite-specific NULL aggregate semantics that differ from ETL-SQL
    - `index/between/`, `commute/`, `delete/`, `in/`, `orderby/`, `random/`, `view/` — all files are 0 bytes (placeholder stubs, never populated)
  - **Current results**: `CorpusRegressionTests` (6 targeted hand-crafted tests): 6/6 pass. `RunAllSltTests` requires `ETL_SQL_RUN_SLT=1` due to OOM risk on select4/select5.
  - **Release validation decision**: `CorpusRegressionTests` (Category=SLT, no env var needed) is the CI gate. Full corpus run (`scripts\Test-SltCorpus.ps1`) is manual pre-release only — run it, then `Parse-SltResults.ps1` for the summary.
