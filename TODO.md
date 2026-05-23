# ETL-SQL Development
## Additions
- [x] **SET SHOW_SECRETS**  It was thought that SET SHOW_SECRETS is a better naming over SET SHOW_PASSWORDS can we just make them aliases of each other but we'll use SHOW_SECRETS as the preferred.

- [ ] **Expand `etl-sql doctor` into a real install validation command**
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

- [x] **Connector certification matrix**
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

- [ ] **Real SFTP integration test lane**
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

- [ ] **Scale certification suite for batching/spilling claims**
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

- [ ] **Persistent lineage and stewardship catalog**
  - Current state: runtime/static lineage, tags, `LINEAGE`, `LINEAGE_TAGS`, tag functions, report dataset/visual lineage analysis, and OpenLineage export exist.
  - Missing product-level stewardship questions:
    - What scripts write to this table?
    - What reports use this dataset/table/column?
    - What jobs touched PII-tagged columns this week?
    - Which outputs were derived from a given source file?
  - Add persistent cross-run lineage storage, likely owned by Orchestrator/Portal or a shared catalog service.
  - Add query/script commands:
    - `SHOW LINEAGE HISTORY FOR TABLE ...`
    - `SHOW LINEAGE HISTORY FOR TAG ...`
    - `SHOW REPORT DEPENDENCIES` should include lineage/tag context where available.
  - Add portal/report views for lineage and tags, not just ETL output tables.
  - Add tests for `CREATE DATASET`, `CREATE VISUAL`, `RUN SCRIPT`, published bundle, and scheduled job lineage continuity.

- [ ] **Documentation truth and findability audit**
  - Goal: separate current reference from roadmap/strategy so users do not mistake planned behavior for implemented behavior.
  - Audit docs for stale backlog language and moved features.
  - Mark strategy docs clearly as roadmap/non-reference where appropriate.
  - Add parser-backed doc checks:
    - Every syntax example in Grammar/Syntax_Index/help parses.
    - Every documented keyword has a help entry or explicit reason it does not.
    - Every help entry links back to the canonical reference page.
    - Samples referenced in docs exist.
  - Add a docs landing map by user goal:
    - "Move data like SSIS"
    - "Build reports like SSRS/Power BI"
    - "Schedule/orchestrate jobs"
    - "Secure scripts for source control"
    - "Track lineage/tags"
    - "Troubleshoot install"

- [ ] **Report Portal operational hardening review**
  - Current state: portal smoke tests pass for publish/execute/snapshot basics, and portal has health checks.
  - Review and test real operational behavior:
    - Snapshot refresh failures keep last known good snapshot.
    - Concurrent refresh/view behavior.
    - Subscription parameterization and per-recipient saved views.
    - Permission edge cases across folders, reports, datasets, and exports.
    - Orchestrator unavailable/degraded states.
    - Report history/error surfaces are readable without horizontal scrolling where possible.
    - Audit log includes report view/export/subscription events.
  - Add a small "portal production readiness" checklist to administrators docs.

- [ ] **Connector certification gap remediation** *(see `Docs/Standards/Connector_Certification_Matrix.md` for full detail)*

  **High risk**
  - [x] **XML streaming refactor** — XML connector accumulates the full document in a DOM before yielding rows (Rule 7 violation). Refactor to streaming `XmlReader` so large XML files do not materialize fully in memory.
  - [ ] **BigQuery CI tests** — Smoke test, credential masking test (T3), and exception wrapping test (T4) are missing. Add a Testcontainers-based BigQuery emulator or GCP sandbox CI step. Currently rated **Preview**; must reach GA before enabling by default.

  **Medium risk**
  - [x] **Exception wrapping tests (T4) — 11 connectors** — T4 tests added for ORACLE, ODBC, EXCEL, PARQUET, AVRO, FTP, AZURE_BLOB, API, SMTP, REPORTPORTAL, and ORCHESTRATOR in `ConnectorExceptionWrappingTests.cs`. All 11 pass.
  - [ ] **SMTP Docker smoke test** — SMTP delivery is untested in CI. Add a `MailHog` or `Greenmail` Testcontainer fixture (similar to the new `SftpFixture`) and cover at least one successful send and one credential-error negative path.
  - [ ] **AZURE_BLOB negative credential and path tests** — Negative auth tests (bad SAS token, expired key) and ResolvePath boundary tests are missing. Add alongside the existing blob smoke tests.

  **Low risk / documentation**
  - [x] **ODBC — document GetExcludedKeywords accepted exception** — Explicit override with comment added to `OdbcConnector.cs`.
  - [x] **Excel — document async accepted exception** — Comment added to `ExcelDataSource.cs` at the `AsDataSet` call.
  - [ ] **Snowflake ADC/JWT auth — CI verification** — Application Default Credentials and JWT key-pair auth are implemented but not CI-verified (no Snowflake test account in the pipeline). Add a CI step or mock-based test that exercises the auth handshake, and document the manual verification steps needed for a full production sign-off.

## Bugs
### VS Code
- [x] **Password not working**  In VS Code I added a password to encrypt a connection.  When I reopened the file and ran the script vs code asked for the password.  I put it in and it gave me this error: ETL-SQL password: requires an interactive console.
- [x] **Test coverage slipped below 70%** — Back to 70.8% after T4 exception wrapping tests were added.
### General
- [ ] **Is SLT corpus complete** It seems like its only SELECT queries but I thought there was a lot more of them.  Can we validate we have a complete SLT test suite.
  - Current state: `tests/slt_data` contains many large corpus/index/evidence files, including SELECT, DML, view/drop evidence, and generated index corpus files.
  - Current runner excludes trigger files, `slt_good_2.test`, `slt_lang_aggfunc.test`, and `select4_debug.test`.
  - SLT is skipped unless `ETL_SQL_RUN_SLT=1` is set and is not part of normal CI.
  - Need to document:
    - Upstream corpus source/version.
    - Which files are included.
    - Which files are excluded.
    - Why each exclusion exists.
    - Current pass/fail count by category.
  - Add a summary artifact from `scripts\Test-SltCorpus.ps1` / `Parse-SltResults.ps1`.
  - Decide whether release validation requires full SLT or a curated representative SLT lane.
