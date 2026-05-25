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

- [x] **`etl-sql doctor` remaining full-profile checks**
  - Confirmed full profile already includes a real report manifest build smoke and PDF export smoke.
  - Confirmed full profile already includes optional Graphviz and browser runtime capability checks.
  - Added optional full-profile service probes for configured Report Portal `/health`, Orchestrator `/health`, SMTP, SFTP, and Azure Blob endpoints.
  - Added explicit coverage for `doctor --json`, `doctor --strict`, `doctor --profile quick|full` parsing and strict exit-code behavior.

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

- [x] **Scale certification suite — remaining coverage**
  - Status review 2026-05-24: added CUBE grouping-set spill certification, scalar subquery cache certification, non-persistent spill cleanup assertions after success and forced failure, tier-derived managed-memory bounds, `FullyMaterializingDml` warnings for uncapped `MERGE`/`UPDATE`/`DELETE` paths, explicit Standard/Stress trait wrappers, and a local Provider lane for CSV/Parquet-backed scale scenarios. External service-backed provider certification remains tracked under connector certification.

- [x] **Report dataset metadata row count certification**
  - Fixed batched `SELECT INTO` and `INSERT INTO ... SELECT` writes so handlers pass the full batch stream to `WriteBatches` once instead of calling append once per batch. This preserves all Parquet row groups for report dataset cache writes.
  - `Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum` now runs in the scale lane and verifies a 50k-row `CREATE DATASET` Parquet snapshot/reload with row count and checksum.

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

- [x] **Persistent lineage and stewardship catalog — report/portal integration**
  - Added executed-script lineage records for runtime `CREATE DATASET` and `CREATE VISUAL` so report objects can be persisted by the existing lineage catalog path.
  - Added lineage/tag enrichment to report dependencies: `/api/reports/{id}/dependencies`, `SHOW REPORT DEPENDENCIES`, and the report viewer Dependencies modal now expose script-derived lineage entries.
  - Added lineage catalog persistence for in-process portal report executions/refreshes after snapshot rebuild.
  - Added publish-time lineage catalog persistence for bundle files in `SQLiteJobHistoryStore`, covering local and remote Orchestrator bundle publishes.
  - Added stable report lineage job names for in-process and remote Orchestrator ad-hoc report jobs (`report:<id>:<session>`).
  - Added authenticated portal catalog lineage APIs for table, source, source file, tag, and job history, with report context attached for `report:<id>:<session>` lineage runs.
  - Added a portal Lineage catalog view with target/source/source-file/tag/job queries, column and date filters, tags, jobs, source files, report links, CSV export of displayed results, and reusable saved query presets.
  - Future consideration: promote local saved query presets to shared/server-side stewardship views if teams need cross-user publishing.
  - Add tests answering stewardship questions:
    - What scripts write to this table?
    - What reports use this dataset/table/column? (API and UI query coverage added, including target-column filter.)
    - What jobs touched PII-tagged columns this week? (API date filters and UI query coverage added.)
    - Which outputs were derived from a given source file? (API and UI query coverage added.)

- [x] **Documentation truth and findability audit — phase 1**
  - Fixed stale "Phase 7 (view transparency)" backlog reference in `Docs/Reference/Lineage.md`; replaced with current engine behaviour note.
  - Confirmed `Docs/README.md` landing map covers all 6 user-goal entries (SSIS/ETL, reports, scheduling, source control, lineage, troubleshooting).
  - Confirmed `Docs/Strategy/README.md` classifies all strategy docs as roadmap/non-reference.
  - Added `tests/ETL-SQL.Tests/Docs/DocSanityTests.cs` with three tests (all pass):
    - `SampleFiles_ReferencedInSampleGuide_AllExist` — every `../samples/…` link in `Sample_Guide.md` exists on disk.
    - `Grammar_SqlBlocks_ParseWithoutSyntaxError` — every non-placeholder `sql` block in `Grammar.md` (138 total) parses without `SyntaxException`/`ParseException`.
    - `HelpFiles_AllNonEmpty` — every `.md` file under `Resources/Help/` is non-empty.

- [x] **Documentation truth and findability audit — remaining checks**
  - Added focused doc sanity coverage that parses SQL blocks in `Syntax_Index.md` and all bundled help files, using the same syntax-failure guardrail as `Grammar.md`.
  - Added focused doc sanity coverage that verifies every `Syntax_Index.md` help link resolves to an existing file or uses an explicit no-help marker.
  - Fixed stale operation help links for file, directory, key-pair, and Docker commands in `Syntax_Index.md`.
  - Added a focused stale-roadmap/backlog language guardrail for reference docs and the Report-SQL guide.
  - Added canonical reference backlinks to bundled help entries and a doc sanity guardrail that enforces the category-level reference mapping.

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
  - [x] **Snowflake ADC/JWT auth — CI verification** — Fixed a recursive `SnowflakeDataSource.CreateCommand(conn)` StackOverflow bug (was calling itself instead of `conn.CreateCommand()`). Added `SnowflakeDataSourceTests`: host allowlist enforcement (SecurityException), JWT connection string authenticator properties, host suffix normalization logic. Full production sign-off still requires a real Snowflake account (see doc comment in `SnowflakeConnectorTests.cs` for manual steps).

## Bugs
### VS Code
- [x] **Password not working**  In VS Code I added a password to encrypt a connection.  When I reopened the file and ran the script vs code asked for the password.  I put it in and it gave me this error: ETL-SQL password: requires an interactive console.
- [x] **Test coverage slipped below 70%** — Back to 70.8% after T4 exception wrapping tests were added.
### General
- [x] **Is SLT corpus complete** — Audited, cleaned up, and documented in `Docs/Standards/SLT_Coverage.md`.
  - **Corpus**: select1–5 from the SQLite Logic Test suite (~9,700 query records). Strong SELECT, JOIN, aggregate, subquery, NULL, and CASE coverage. See the coverage doc for the full confidence matrix.
  - **Exclusions documented**: `select4_debug.test` (truncated artifact, deleted), trigger files (deleted — no trigger support), `slt_lang_aggfunc.test` (SQLite-only by design: tests `total()`, `group_concat()`, non-numeric-to-0 coercion — all inapplicable to ETL-SQL), `index/` subdirectory (real SLT index-optimization tests retained in repo but excluded from runs — use `CREATE INDEX` on regular tables, not supported in ETL-SQL).
  - **Gap**: DML (UPDATE/DELETE complex forms, INSERT SELECT, MERGE) is lightly covered — evidence files test basic paths only. Suggested additions tracked below.
  - **Release validation**: `CorpusRegressionTests` (6 hand-crafted tests, ~2s) is the CI gate. Full corpus run via `scripts\Test-SltCorpus.ps1` is manual pre-release.

- [x] **SLT DML coverage gap** — added `dml.test` (UPDATE: arithmetic, CASE-in-SET, subquery-in-WHERE, multi-column, unconditional, no-op; DELETE: WHERE, subquery, no-op, unconditional), `insert.test` (INSERT VALUES with NULL and expressions, INSERT SELECT filtered, with JOIN, with aggregate), and `merge.test` (upsert, conditional WHEN MATCHED AND, inventory top-up). Also added `MergeStatementHandler` to SltRunner — it was missing from the handler list, blocking MERGE tests. All 40 SLT files pass.
