# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions. Version numbers follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

**Practical High Availability — PostgreSQL State Provider**
- Made both the Portal (EF Core) and Orchestrator (hand-written) state stores **provider-selectable** between SQLite (default, unchanged) and PostgreSQL via configuration (`Portal:Database` / `Orchestrator:Database` Provider + ConnectionString), removing the previously hardcoded SQLite coupling.
- Implemented PostgreSQL end to end for both stores, verified against a real Postgres via Testcontainers: the Portal gained a dedicated migrations assembly for Postgres, and the Orchestrator store became a provider-neutral `RelationalJobHistoryStore` behind a dialect (portable SQL, with a Postgres `nocase` ICU collation backing `COLLATE NOCASE`).
- Added `etl-sql admin migrate-database --from sqlite --to postgres [--dry-run]` to copy existing single-node SQLite Portal/Orchestrator state into the configured PostgreSQL deployment: values are coerced to each target column's type, foreign-key ordering is bypassed for the load, identity sequences are resynced, and per-table row counts are verified — any mismatch fails closed (nothing is committed). `--dry-run` verifies counts and target-schema compatibility without writing.
## [0.12.0] — 2026-06-19

### Added

**Job-Scoped State Persistence & Incremental Watermarking**
- Implemented `GET_JOB_STATE(key)` and `SET_JOB_STATE(key, value)` primitives for scheduled and ad-hoc incremental data loads.
- Buffered state updates during execution, committing them atomically to the orchestrator store (SQLite or PostgreSQL) only upon successful script completion.
- Added a developer CLI fallback that persists state in local `[script_name].etlstate` JSON files.

**JSON/Spec-Backed Schema Contract Checks**
- Extended the `EXPECT SCHEMA` syntax to validate schemas using a reviewed JSON specification contract file: `EXPECT SCHEMA target FROM 'path/to/spec.json' [ON DRIFT WARN];`.
- Added support for verifying column presence, type family matching, nullability constraints, string length limits, and decimal precision/scale settings loaded from the JSON `"schema"` array, respecting `context.ResolvePath()`.

**Certified OpenID Connect (OIDC) Authentication**
- Implemented federated login, logout, and token refresh in the Report Portal with support for external Identity Providers.
- Hardened user account binding by keying local profiles to the immutable OIDC `sub` (subject) claim to prevent takeover risks if usernames/emails are reassigned.
- Added dynamic group mapping to synchronize identity provider role/group claims to local Report Portal user groups at login.
- Added configuration diagnostics and redacted status checks to ensure OIDC provider availability can be monitored without exposing client secrets.
- Certified recovery scenarios (IdP outages, JWKS key rotation, claim modifications, and token revocation) with a robust integration test suite.

**VS Code Extension Enhancements**
- Cleaned up ESLint static analysis and type declarations across TypeScript sources.
- Stabilized the extension integration test suite by tuning Mocha bootstrap timeouts to accommodate headless environment activation delays.

### Changed

**Pushdown Aggregation & Staged Extracts**
- Enabled SQL pushdown for eligible `SELECT ... INTO #temp` queries containing `GROUP BY`, aggregates, `DISTINCT`, and compatible joins. Pushes aggregation down to the source database and streams only grouped/filtered results back.

**Cross-Connection Semi-Join Pushdown**
- Added an optimizer that rewrites joins between small local temp tables (1-1000 rows) and large remote SQL tables to push a parameterized key filter (`IN` clause) directly to the remote query, preventing full-table memory loading.
- Optimized compiling of the query key list using driver-parameterized values (`@p0`, `@p1`, etc.) to leverage caching and prevent injection, with plan visibility under `[SEMI-JOIN PUSHDOWN ON ...]`.

**Evaluator Performance Enhancements**
- Optimized hot-path identifier and column resolution by switching to allocation-free `Row.TryGetValue` instead of copying new row columns dictionaries, saving significant heap allocation during streaming query execution.
- Avoided redundant column lookups during variable and identifier evaluations using a unified `TryResolveIdentifier` check.

## [0.11.0] — 2026-06-14

### Added

**Secure Datasets**
- Reworked the DATASET subsystem for multi-user safety: globally unique dataset names with stable-Id storage paths, dataset→folder linkage where `PUBLIC` resolves to folder-read permission, and caller-identity threading that closes an ACL bypass.
- Added portal-managed at-rest encryption for the dataset cache (parquet encrypted at rest), failing closed on a missing or weak at-rest key, with at-rest key rotation and a verification deck.
- Added `EXPORT DATASET` (a portable transport-encrypted copy) and `PUBLISH DATASET` (import a portable file and re-encrypt at rest).
- Added serve-stale-with-warning behavior plus an editor/owner refresh gate, refresh triggers, and authorization/atomicity hardening.

**Script-First Portal Reconstruction**
- Added `EXPORT PORTAL CONFIGURATION` to export users, groups, memberships, folders, ACLs, report publications, dataset metadata/grants, SMTP aliases, subscriptions, and alerts as a versioned, idempotent `.etlsql` bootstrap script that emits logical names (never database IDs).
- Excluded all credentials, keys, and cached values from the export, emitting `${...}` secret placeholders with a generated requirements header.
- Made bootstrap import deterministic and rerun-safe (create-or-skip by logical name) with `SET WHAT_IF ON` dry-run validation that fails closed on missing secrets or references.
- Added a companion content manifest / recovery runbook, and an automated clean-server round-trip reconstruction proof.

**Multi-User Correctness & Recovery**
- Fixed the folder/asset ownership lifecycle (ownership now implies Manage) with explicit ownership transfer/reassignment before user deletion.
- Made audit recording part of the operation contract: security-sensitive mutations and their audit rows now commit atomically, with correlation IDs for background work and opt-in retention.
- Added a durable per-job execution lease (Orchestrator), a recoverable subscription lifecycle, and a durable subscription delivery ledger with at-most-once semantics and idempotency/failure tests.
- Added per-user execution fairness limits, scriptable SMTP connection management, refresh-token reuse detection/purge with cached-token validation, and bounded report-snapshot retention.

**Operator Tooling (CLI)**
- Added an `etl-sql admin` command group with `admin doctor` (a backward-compatible alias of `doctor`) and `admin support-bundle`, which produces a credential-redacted archive (config, health snapshot, recent logs, database metrics).
- Added `etl-sql init` to scaffold a starter configuration (with a generated JWT secret) and a first runnable `.etlsql` script for CLI-first onboarding.
- Added `etl-sql admin backup` (split-custody data + keys archives) and `etl-sql admin restore` with fail-closed `--validate` (matching backup-id pair, key-version coverage, per-file checksums, and version compatibility).
- Surfaced database schema migration status on the operational metrics endpoint, and wired the N→N+1 in-place upgrade-path drill into `Test-PreRelease.ps1` as a release gate.

**Verification & Observability**
- Added a hosted-service integration lane, genuine multi-process coordination tests, fault-injection/recovery tests, an automated backup/restore drill, and an admin operational metrics endpoint (queue depth, active executions, failure rates, dataset/snapshot disk usage).

**Language & Engine**
- Added inline tags in `CREATE TABLE` and `INT(N)` fixed-width digit precision.
- Added a memory-grant arbiter, tag value validation, and lineage cycle warnings.

### Changed

- **Licensing:** Relicensed ETL-SQL from PolyForm Noncommercial 1.0.0 to the Apache License 2.0 and aligned the installer, VS Code extension metadata, bundled browser assets, contribution policy, and public documentation.
- **Documentation validation:** Added connector-aware checks for `CREATE CONNECTION` examples so unsupported option names and published option values fail the documentation test suite instead of passing grammar-only validation. Connector metadata now exposes supported named `PATH`, `HOST`, and flat-file truncation options used by public examples.
- Formalized automatic SQLite schema migrations on Portal startup: the applied migration set is logged and a migration failure now fails fast rather than serving a half-migrated catalog.
- Realigned the `CREATE` `ENCRYPT` clause as transport-only and removed the cleartext-credential dataset-refresh sidecar.
- Adopted an optimistic-concurrency contract for concurrent administration, batched dataset-listing permission checks for performance, and refreshed branding, trademark, logo, and README positioning.

### Fixed

- Resolved FLATFILE connectors with EXCEL/JSON/XML/PARQUET/AVRO formats to their correct dialects in `PipelineGenerator`, and fixed a `FlatFileDataSource` compiler error.
- Fixed `SessionCache` race leaks and stale admin caller context, a refresh debounce race, and disabled accounts surviving LDAP login; removed the hardcoded first-run admin password.
- Corrected dataset at-rest encryption metadata to be truthful, required Manage to change dataset access level, and regenerated the dataset-refresh-permission migration via EF tooling.

### Security

- Backup secret artifacts (keys archive, key ring, re-injected config) are written with owner-only permissions, and backup manifest validation rejects path-traversal entries.
- Hardened portal sessions and anonymous delivery, added authentication rate limiting and a content security policy, and added runtime secret rotation.
- Closed authentication, SSRF, injection, key-handling (.p8), and audit release blockers; added Dependabot for the NuGet and npm ecosystems.

## [0.10.0] — 2026-06-08

### Added

**Experimental: Specification-Driven Development (Beta)**
- Added `gen-script` CLI command to compile standardized JSON specification contracts into ETL-SQL starter scripts. Generated templates include source layout review notes, confidence/source-evidence comments, casting expressions, inline lineage tags, `EXPECT SCHEMA` gates, validation issue summaries, optional quarantine tables, and outbound load scaffolding.
- Added `extract-spec` CLI command utilizing PDFsharp to automatically trim and extract data dictionary pages from large vendor PDF documents using heuristic keyword scoring.
- Added workflow guide `Docs/Reference/Spec_Driven_Development.md`, prompt instruction guide `Docs/data_spec_parser_instructions.md`, machine-readable contract `Docs/Reference/spec_pipeline.schema.json`, and Cookbook recipe 25 with a runnable customer-feed example.
- Added [PipelineGenerator](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.App/App/PipelineGenerator.cs#L14) and [SpecExtractor](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.App/App/SpecExtractor.cs#L12) test suites under `tests/ETL-SQL.Tests/App/` covering contract validation, generated-script parsing, review metadata, validation gates, and PDF trimming scoring.
- *Note on limits*: This is a developer productivity feature, not an automated production-pipeline generator. LLM spec parsing and vendor formats are variable; generated scripts are intended as reviewed starting points. Developers must verify the JSON, complete the extraction query, review evidence/low-confidence fields, and test against real vendor files.

**Terminal IDE (TUI) Modernization**
- Implemented collapsible sidebar file explorer tree and tabbed multi-file support in [ConsoleEditor.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/ConsoleEditor.cs#L29).
- Added support for multi-cursor editing, F1 help dialog shortcuts, and drag-to-select text in the editor.
- Added in-editor text find/search with result highlighting and `F3`/`Shift+F3` navigation.
- Added live query diagnostics while editing and visual gutter diagnostic markers.
- Added non-blocking, cancellable script execution, allowing queries to run asynchronously in the background.
- Added a Schema Explorer in the sidebar showing database tables and views with lazy loading support.
- Added a Variables explorer tab in the bottom pane matching the VS Code Variable Explorer functionality.
- Added query result-cell navigation and inspection, along with cell-value inspection popups.
- Added automatic workspace persistence and recovery, preserving open files and tabs across TUI restarts.
- Added customizable JSON-based editor themes with a preset theme library and `F3` theme-cycling hotkey.
- Re-implemented robust console keyboard input via Win32 ReadConsoleInput, resolving terminal input lockups.
- Added per-tab caching for query results, execution messages, active execution tree, and performance metrics.
- Added a new `rollback-all-transactions` command to abort all active transactions.
- Added an Output tab to act as a durable, clickable home for served URLs and export paths.
- Added custom terminal rendering features including braille line charts, fractional-block bar charts, buttons, containers, and `RELDATEPICKER` controls.
- Added a TUI Command Palette (`Alt+P`) and support for exporting reports directly to Markdown or PDF.
- Added a `serve` utility (`Ctrl+Shift+R`) to run report previews directly in the browser via dynamic self-invocation, supporting serve-folder multi-report launching.
- Added Publish to Portal support (matching VS Code publish features) and connection reset commands.

**Connectors & Integrations**
- Added a native **Neo4j** graph database connector supporting key merging, validation, and metadata queries (see [Neo4jConnector.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Neo4j/Neo4jConnector.cs) and [Neo4jDataSource.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Neo4j/Neo4jDataSource.cs)).
- Added outbound writing support and completed production gaps for the REST API connector.
- Enhanced Azure Blob, SFTP, S3, and local Directory connectors to include fallback decryption and structured path parsing.

**Language, Lineage & Governance**
- Added `CREATE TAG` and `CREATE LINEAGE FROM ...` syntax to support programmatic importing of curated lineage assets and metadata tags.
- Added the `DIFFERENCE(s1, s2)` Soundex similarity scoring string function (see [FuzzyFunctions.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Functions/FuzzyFunctions.cs)).
- Added a cross-platform CLI `etl-sql purge` command for cleaning up old data and session histories.
- Expanded SQL Logic Test (SLT) coverage for index creation, table truncation, table alteration, `LEFT SEMI`/`LEFT ANTI` joins, and `QUALIFY` statements.

**Verification & Orchestration Hardening**
- Added job scheduler chaos coverage and concurrency race verification tests (scheduler, subscription, and active-work).
- Added a subscription delivery diagnostics UI and preserved subscription failures in the history store.
- Added verification tests for Report Portal user permission models and user workflows.
- Added a new capacity planning guide (`Docs/Strategy/Capacity_Planning.md` or similar) and published service capacity baselines.
- Added capacity workload templates and row-volume capacity planning profiles.
- Added scaling tests for portal administration catalogs and enterprise identity lifecycle verification.

### Fixed
- **Query Parser:** Fixed parser bugs for `LEFT SEMI`/`LEFT ANTI` joins and tolerated trailing semicolons (`;`) for statements inside `BEGIN`/`TRY` blocks.
- **Cookbook Recipes:** Audited and fixed all 23 Cookbook recipes to ensure they compile and parse cleanly, fixing issues with `ENCRYPT`, `SEND EMAIL`, `EXEC`, `DECLARE`, and deprecated `WITH PARAMETERS` report options.
- **TUI Editor:** Implemented file overwrite warnings when a file changes on disk, fixed sidebar layout wipeout during redraw by clearing partial line width, and resolved keyboard input lockups on Windows.
- **TUI Autocomplete:** Fixed snippet triggers (`$mssql`) showing inside the autocomplete suggestions and prevented crashes when brackets appeared in prompt titles.
- **TUI Metadata:** Restored temp table querying inside [TuiMetadataManager](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L106).
- **Report Preview:** Fixed report preview wrapping bugs, added rounding for Card/Table numbers, and added page navigation arrows via keyboard/mouse.
- **Test Integrity:** Resolved parallel test conflicts in Neo4j tests, and excluded Docker LDAP portal tests from non-Docker lanes.

### Changed
- **Dependencies:** Upgraded `SQLitePCLRaw` package reference to `3.0.3` to resolve pre-release auditing and scoped it exclusively to Core instead of globally.
- **Code Refactoring:** Refactored `ConsoleEditor` dependencies to use dependency injection instead of service-locating patterns.
- **Platform Infrastructure:** Hardened shell scripts and systemd unit files to use Unix LF line endings.
- **Packaging:** Brought the Linux `.deb` installer to parity with the Windows MSI (including uninstall prompts and service configuration) and published VSIX as a standalone asset.
- **Release Tooling:** Made the pre-release NuGet dependency audit reliable on the pinned .NET 10.0.300 SDK with central package management — solution-level `--deprecated`/`--vulnerable` checks fall back to per-project auditing and fail with an actionable message rather than silently skipping when no authoritative audit can run.

### Security
Hardening from the v0.10.0 release-readiness security review:
- **Orchestrator API authentication:** The ad-hoc job API (`POST /jobs`, `DELETE /jobs/{id}`, `GET /jobs/{id}`) now requires the `X-Orchestrator-Key` header like the scheduled-job and management routes; only `/health` and `/metrics` remain open. The service fails fast at startup when no API key is configured while bound to a non-loopback address, and the MSI/Linux installers generate and mirror matching `Orchestrator:ApiKey` / `Portal:Orchestrator:ApiKey` values.
- **Spec module injection:** Restricted spec dataset names to a documented safe-identifier format, normalized each generated module path to stay within the modules directory, and escaped generated ETL-SQL string literals — preventing path traversal and ETL-SQL injection in `gen-script` output.
- **REST egress / SSRF:** Disabled automatic HTTP redirects in the REST connector; redirects are now followed explicitly with a bounded count, every hop's host is re-validated against the egress allowlist, and credential headers are stripped on cross-host or HTTPS→HTTP redirects.
- **Path Validation:** Enforced zero-trust path validation for the Snowflake `PRIVATE_KEY_FILE` option while accepting the documented `.p8` PKCS#8 key extension.
- **Token Permissions:** Restricted portal token file permissions strictly to the owner.

---

## [0.9.0] — 2026-06-01

### Added

**Reporting: Export Fidelity**
- Server-side ECharts SSR export path: report chart visuals can render real ECharts output into SVG for PDF generation.
- PDF export now includes chart-rendering coverage through `EChartsSsrRenderer` and `PdfExporter` tests, including a PDF magic-header assertion and chart visual rendering path.
- Markdown/table export formatting tightened through the shared report cell formatter so exported tables preserve cleaner display values across report outputs.

**Language: Pipeline Checkpoint / State Resume**
- `LabelName:` syntax as `SectionLabelStatement` — top-level labels auto-serialize `#temp` table contents (Apache Arrow spill) and variable scope (JSON) as named checkpoints.
- `GOTO LabelName;` control-flow statement with full scoping guardrails: GOTO may jump OUT of nested loops, conditionals, and `TRY…CATCH` blocks; jumping INTO nested blocks is a compile-time error; cross-script jumps blocked.
- `--session <id>` and `--resume` CLI flags: `--session` names the state store; `--resume` restores the most recent checkpoint and skips already-completed labels. Passing `--resume` without `--session` or without a saved checkpoint is a fail-fast error.
- LSP: section labels exposed in document outline for folding and symbol navigation; `GOTO` autocomplete lists reachable label names.
- Grammar, User Manual, and Specialized_Operations.md updated with label/GOTO syntax, scoping rules, and `--resume` CLI reference.

**Connector: Native MySQL / MariaDB**
- `MySqlConnector` provider built on the `MySqlConnector` NuGet package — eliminates the ODBC bridge dependency, delivers native dialect parsing, and wraps all provider exceptions as sanitised `ExecutionException`s at the connector boundary.
- Procedure/routine metadata discovery via `MySqlCatalogProvider`.
- Dedicated `MySqlFixture` / `[Collection("MySQL")]` so non-MySQL database tests no longer pay MySQL container startup cost.
- Third-party inventory updated with MySqlConnector 2.3.7 and Testcontainers.MySql 4.11.0.

**Diagnostics: EXPLAIN / EXPLAIN ANALYZE**
- `EXPLAIN <statement>` produces a query-plan table (ID, Operation, Details, Cost, Mode, Est. Rows).
- `EXPLAIN ANALYZE <statement>` adds Actual Rows, Actual Time, and Spill (bytes) columns by executing the statement under instrumentation.
- Available as a `--explain` CLI flag for whole-script plan output.

**Observability: Spill & Memory Metrics**
- `--perf` summary table now includes a "Disk Spilled: X MB" row.
- `--verbose` JSON telemetry packet includes `spilledMb`.
- `SHOW PROFILE` tracks `SpilledBytes` per statement alongside elapsed time and row counts.
- `ExecutionTelemetryManager` exposes `TotalSpilledBytes`, `SubquerySpilledBytes`, and `SortSpillCount` for downstream reporting.
- `Docs/Reference/Performance.md` (new): all four external engine thresholds and activation conditions, `SET` threshold overrides, `appsettings.json` defaults, spill storage and encryption, observability reference, memory model, tuning guidance table, and scale certification tier definitions.

**Governance: Execution Audit Log for Ad-Hoc Runs**
- `Engine:AuditAdHocRuns` appsetting (default: `false`) gates audit logging for standalone `--run` executions.
- When enabled, `EngineRunner` calls `IJobHistoryStore.LogJobStartAsync` / `LogJobEndAsync` so script runs appear in the Orchestrator execution history alongside scheduled jobs.

**Release Infrastructure**
- `scripts/Test-PreRelease.ps1`: local pre-release validation runner with resumable phases (source-hash fingerprinting prevents reusing stale results after code changes). Phases: sync-assets drift, restore, build, smoke/fast test lanes, Node.js unit tests, sample smoke, Smoke-tier scale cert. Optional switches: `-IncludeDockerIntegration`, `-IncludeStandardScale`, `-BuildInstallers`, `-SkipNode`, `-SkipScale`, `-Resume`.
- `scripts/Compare-CertBaseline.ps1`: diffs a `cert-report.json` against a stored baseline — exact pass/fail, result-row count, checksum, and elapsed-time regression (±50% threshold). Exits 1 with a regression table on any failure.
- `Docs/Strategy/Release_Capability_Matrix.md`: release claim matrix tying public product claims to concrete evidence and preventing release notes from overstating tested behavior.
- `scripts/Get-TestLaneInventory.ps1`: static lane inventory report showing discovered xUnit tests by lane, category trait, project, and fast-lane exclusion reason.
- `perf` lane now runs engine hardening performance tests plus the dedicated perf project; `fast`, `portal`, and `full` lanes include the Node lineage UI smoke test.
- Scale certification baselines committed: `certification-results/baseline-smoke.json` (Smoke, 1×) and `certification-results/baseline-standard.json` (Standard, 10×, 13 scenarios, all passing).
- `.github/CODEOWNERS` and Dependabot configuration added.
- Four GitHub workflow templates under `.github/workflow-templates/` (local-validated-release, manual-docker-certification, manual-release-validation, manual-scale-certification) — staged for future activation; not yet wired to automatic triggers.
- `Docs/Strategy/Release_Workflows.md` documents the local-first release ownership model and workflow template activation guide.
- Windows release packaging scripts hardened for reliable local/CI builds: resolved WiX tool lookup, WiX 3.x Program Files discovery, explicit MSI failure handling, and local validated release workflow WiX installation.

**Documentation**
- `Docs/Architecture/Lineage.md` (new): what is tracked, `LineageEntry` data model, `SHOW LINEAGE` syntax variants, Mermaid and OpenLineage export, `SHOW LINEAGE HISTORY` cross-run catalog, metadata inheritance rules, and Orchestrator (`etlsql.db`) integration.
- `Docs/Reference/Performance.md` (new): see Observability above.
- `Docs/Strategy/Release_Workflows.md` (new): see Release Infrastructure above.
- Architecture documentation expanded for connector, engine, expression evaluation, language server, lineage, orchestrator, parser/lexer, portal UI, report portal, reporting, TUI editor, variable scoping, and VS Code extension boundaries.
- `Docs/Testing.md`, `Docs/Strategy/Test_Strategy.md`, and `scripts/README.md` reorganized around the current lane model, pre-release phases, SLT usage, coverage expectations, and installer prerequisites.
- Connector standards and reference docs corrected for current connector option naming rules, supported connector inventory, and source-boundary guidance.

**Tests**
- `ResumeEdgeCaseTests.cs` — 5 integration tests covering: fail-fast on IsResuming without checkpoint; fresh-variable guarantee on `--session` without `--resume`; GOTO keyword-target parse diagnostic; SaveSession graceful return for non-Evaluator contexts; mid-script resume uses loaded checkpoint state.
- `ParserErrorQualityTests.cs` — 17 parameterized cases across 4 constructs (GOTO, CREATE CONNECTION, SEND EMAIL, RUN SCRIPT) asserting error messages name the construct and expected token.
- `ExampleOutputCorrectnessTests.cs` — 6 assertion-based tests verifying correct output (row counts, column values, specific cell values) for self-contained scripts in `01_Basics/` and `07_Real_World/`: function library, window deduplication, incremental MERGE, data masking, anti-join reconciliation, and PIVOT.
- `CrossHostConsistencyTests.cs` — verifies that the same `.rptsql` fixture produces identical manifest structure (title, visual count, visual names, row counts, column names) when executed via `DashboardService` directly and via the Portal API execute → snapshot path.
- `MySqlTests.cs` — Docker real-integration tests for the new native MySQL connector.
- ETL scenario golden tests expanded to 27 scenarios covering staged ETL, cleansing, JSON extraction, file round trip, lineage tags/source columns, `WHAT_IF`, loops, `TRY...CATCH`, transactions, DML audit, merge, hash-change detection, set ops, recursive CTE, pivot/unpivot, semi/anti joins, and modular scripts.
- SLT release evidence added for custom ETL-SQL semantics plus the explicit `slt` lane; the release branch SLT lane passed on 2026-06-01.
- Docker-backed integration lane audited and stabilized; the release branch integration lane passed on 2026-06-01 with 97 tests covering connector and platform service boundaries.
- Standard scale certification evidence recorded on 2026-06-01: 13 scenarios passed at 10× row scale.
- Windows package evidence recorded on 2026-06-01: `publish_release.ps1 -Platforms win-x64` produced ZIP/VSIX assets and `build_msi.ps1` produced `ETL-SQL-Enterprise-v0.9.0.msi`.
- UI sandbox and Node smoke coverage added for lineage DAG, designer, script editor, VS Code webviews, datasets admin, and lineage catalog browser-side surfaces.

### Fixed

- **Report export rendering**: PDF chart export now uses the ECharts SSR pipeline so chart visuals render as real chart images; table and filter visual formatting paths were tightened for PDF/Markdown output.
- **VS Code Extension cross-platform hardening**: Added automatic execute permissions setup (`chmod +x`) on Linux/macOS for bundled executables, resolved terminal commands using dynamic shell detection (fixing PowerShell-only `&` operator errors on zsh/bash/cmd), fixed notebook engine lookup in packaged environments, resolved broken welcome links using a GitHub repository fallback in production, added auto-cleanup of temporary scripts, and implemented child spawn error listeners to prevent crashes.
- **`--resume` silently ignored**: passing `--resume` without `--session` would run the full script from the beginning with no warning. Now fails fast with a descriptive error.
- **Stale session state on fresh runs**: `LoadSessionState` fired whenever a `--session` ID was supplied, restoring variables from prior runs even without `--resume`. Now only called when `--resume` is explicitly set.
- **GOTO keyword targets**: the GOTO validation guard used `&&` so keyword tokens (e.g. `SELECT`) passed validation and produced a `GotoStatement` with a keyword target — a silent parse error that deferred to a confusing runtime failure. Targets now restricted to `TokenType.IDENTIFIER`.
- **`SaveSession` ArgumentException on mocks**: `SessionStateManager.SaveSession` hard-cast `IExecutionContext` to `Evaluator` and threw `ArgumentException` for any stub, mock, or sub-evaluator. Now returns early gracefully for non-Evaluator contexts.
- **BigQuery null dereference**: `t.Reference.TableId` in `GetTablesAsync`/`GetViewsAsync` had no null guard; `t.Reference?.TableId` added with a skip on null entries.
- **MySQL double-dispose**: `RollbackAsync` disposed `_transactionalConnection` in its `finally` block then nulled the field; if that `DisposeAsync` threw, the null-assignment was skipped and `DisposeAsync` was called a second time. Connection is now captured locally and nulled before the call in both `CommitAsync` and `RollbackAsync`.
- **Parser error messages**: 12 messages across `DataParser.cs` (CREATE CONNECTION), `ExtensionParser.cs` (SEND EMAIL), and `SystemParser.cs` (RUN SCRIPT) updated to name both the construct and the expected token, matching the quality bar of the core engine.
- **Docker platform service tests**: Report Portal and Orchestrator service Docker tests now build images through a direct `docker build` helper and `.dockerignore` excludes local databases/logs/generated output from build context archives.
- **Windows MSI discovery**: `build_msi.ps1` now detects installed WiX 3.x toolsets under Program Files, including v3.14 installations, before compiling the MSI.

### Security

- **JWT secret hardening**: `JwtSecretValidationService` rejects default or weak JWT secrets at portal startup in production mode.
- **CI workflow hardening**: CODEOWNERS enforces review requirements; Dependabot tracks dependency updates; `sync-assets.js -Check` runs in CI to prevent stale shared report runtime assets from shipping.

---

## [0.8.0] — 2026-05-25

### Added

**Connector Testing & Certification**
- **Connector Certification Matrix**: Formal 4-class certification framework (`MetadataOnly`, `MockedIntegration`, `LocalRealIntegration`, `DockerRealIntegration`) across all 21 connectors. `Connector` and `CertificationClass` traits on every test class enable targeted release gate selection.
- **FTP Docker real-integration**: `delfer/alpine-ftp-server` Testcontainers fixture covering connection, upload/download round-trip, root listing, wrong-password provider-failure wrapping, and `PORT` option handling.
- **REST API real-integration**: Loopback HTTP server tests for PUT and DELETE requests with Basic, Bearer, and API key auth; PUT body verification.
- **Azure Blob (Azurite) integration**: Smoke, upload/list round-trip, download, bad account key, expired SAS token, and host-allowlist enforcement.
- **SMTP (Mailpit) integration**: Docker-backed send-and-verify, multi-row batch, connection-refused and host-allowlist failure paths.
- **BigQuery emulator integration**: `ghcr.io/goccy/bigquery-emulator` Testcontainers coverage for T1 smoke plus T2–T4 unit coverage (invalid credentials, credential masking, host allowlist).
- **Snowflake emulator integration**: Emulator-backed tests plus unit coverage for JWT connection properties, host suffix normalisation, and host-allowlist enforcement. Fixed a `StackOverflowException` in `SnowflakeDataSource.CreateCommand`.
- **Parquet/Avro corrupt-file coverage**: Real-file negative-path reads that verify corrupt provider errors are wrapped as sanitised `ExecutionException`s.
- **Exception wrapping (T4)**: Provider-exception wrapping verified for 11 connectors: ORACLE, ODBC, EXCEL, PARQUET, AVRO, FTP, AZURE_BLOB, API, SMTP, REPORTPORTAL, ORCHESTRATOR.

**`etl-sql doctor` Enhancements**
- `--profile quick|full` — quick profile stays fast; full profile runs report-manifest smoke, PDF export smoke, Graphviz/browser capability checks, and service probes (Report Portal `/health`, Orchestrator `/health`, SMTP, SFTP, Azure Blob).
- `--json` output mode for automation.
- `--strict` flag returns non-zero on warnings.
- Full runtime-path write checks, parser/engine/linter/security/encryption/file/report-asset/Node/portal-DB health probes.

**Scale Certification Harness**
- `scripts/Test-ScaleCertification.ps1` runs smoke/standard/stress tiers with `CERT_ROW_SCALE`-driven row counts.
- Certified scenarios: external sort, aggregate, join, temp-table spill, result cap, window spill, CUBE grouping-set spill, scalar subquery cache, and non-persistent spill cleanup after success and forced failure.
- Each scenario asserts correct row count, `TotalSpilledBytes > 0` for spill paths, tier-derived managed-memory bounds, and cleanup completion.
- `FullyMaterializingDml` warnings for uncapped `MERGE`/`UPDATE`/`DELETE` paths documented with explicit limits.
- 50k-row `CREATE DATASET` Parquet snapshot/reload certified with row count and checksum (`Cert_Smoke_ReportDatasetSnapshotReload_50kRows`).

**Persistent Lineage & Stewardship Catalog**
- `ILineageCatalogStore` interface with `SaveLineageAsync`, `GetHistoryForTableAsync`, `GetHistoryForTagAsync`; implemented in `SQLiteJobHistoryStore` (`LineageHistory` table, auto-migrated).
- New statements: `SHOW LINEAGE HISTORY FOR TABLE <name>` and `SHOW LINEAGE HISTORY FOR TAG <key> [= 'value']`, both supporting `LIMIT` and `INTO #t`.
- Portal Lineage catalog view: target/source/source-file/tag/job queries, column and date filters, tags list, jobs list, source-file links, report links, CSV export, and saved query presets.
- Lineage catalog persistence for portal in-process report executions, bundle publish events, and `CREATE DATASET`/`CREATE VISUAL` runtime events.
- Authenticated portal APIs for table, source, source-file, tag, and job lineage history with report context attached.

**Report Portal Hardening**
- Concurrent snapshot/history/report/list reads during refresh and duplicate-refresh debounce verified by integration test.
- `EXPORT_CSV` and `EXPORT_PDF` audit events added to `ExportController`.
- Read-only report access: snapshot/export allowed, execute/refresh denied, private dataset ACL filtering on dependency and dataset-list endpoints.
- Report history modal updated with dedicated table rendering and horizontal scroll fallback for long hashes.

**Snippet Library Phase 4**
- 13 new built-in snippets covering common connector, lineage, reporting, and scheduling patterns.
- User-defined snippets loaded from disk at startup.
- TUI tab-stop navigation inside snippet placeholders.
- F1 reference integration: snippets surface in `HELP SNIPPETS` and the snippet reference panel.

**Documentation**
- Doc sanity tests: SQL blocks in `Grammar.md`, `Syntax_Index.md`, and all bundled help files parse without syntax errors; help link resolution verified; stale roadmap language guardrail for reference docs.
- Connector Standards doc updated to reflect XML streaming refactor (Rule 7 compliance).
- Scale certification claims page added (`Docs/Standards/ScaleCertification.md`).
- SLT corpus coverage documented in `Docs/Standards/SLT_Coverage.md`.

### Fixed
- **Snowflake StackOverflow**: `SnowflakeDataSource.CreateCommand` was recursively calling itself; fixed to delegate to the underlying connection.
- **VS Code password prompt**: "requires an interactive console" error when an `ENC:`-protected connection was opened in VS Code; password masking now works via the VS Code input mechanism.
- **Test coverage gate**: Coverage had slipped below 70%; restored to 70.8%+ with T4 exception-wrapping test additions.
- **SLT DML gap**: Added `dml.test`, `insert.test`, and `merge.test` to the SLT corpus; `MergeStatementHandler` was missing from `SltRunner` and is now registered. All 40 SLT files pass.
- **Oracle negative-path coverage**: `gvenzl/oracle-free` Testcontainers fixture extended with missing-table and invalid-SQL failure paths.
- **Azure Blob expired SAS**: `AzureBlobIntegrationTests` now generates and tests an expired account SAS token.

### Changed
- **XML streaming refactor**: XML connector refactored from full-DOM accumulation to streaming `XmlReader`, eliminating full materialisation of large XML files (Rule 7).
- **ODBC/Excel async exceptions**: Accepted exceptions documented with inline comments in `OdbcConnector.cs` and `ExcelDataSource.cs`.
- **`SET SHOW_SECRETS`**: `SET SHOW_PASSWORDS` is now an alias for the preferred `SET SHOW_SECRETS` form.
- **`v0.7.0` baseline notes moved**: Migration Guide updated to reflect 0.8.0 as the current baseline.

---

## [0.7.0] — 2026-05-18

### Added

**Reporting & Interactive Dashboards**
- **Advanced Drill-Down**: Implemented `DRILL_IN` and `DRILL_DOWN` for hierarchical, in-place data exploration; added `DRILL_TO` for cross-report navigation with parameter state passing.
- **Paginated Reports**: Support for `PAGINATED = ON` reports featuring automatic header/footer repetition, multi-page data grid spans, and specialized snapshot formats.
- **ETL Notebooks (`.etlnb`)**: Native VS Code notebook support with cell-based execution, stateful REPL persistence, and cross-cell IntelliSense for connections and variables.
- **Cross-Visual Highlighting**: Power BI-style interactive filtering where clicking a chart segment highlights related data across all other visuals.
- **Ghost Rendering**: Enhanced interaction logic with "ghosting" (dimming) support for Line, Scatter, Pie, and Donut charts during highlighting.
- **New Visual Types**:
    - **MAP**: Integrated ECharts-based mapping with custom GeoJSON support (`MAP_FILE`).
    - **Specialized Charts**: Added `GAUGE`, `BOXPLOT`, `WATERFALL`, `BUBBLE`, `RADAR`, and `CANDLESTICK`.
    - **Input Visuals**: Added `TEXTBOX`, `NUMBERBOX`, and `CHECKBOX` for direct scalar parameter input.
    - **Interactive Slicers**: Support for `SLIDER` and `SEARCH` visual types with immediate dashboard re-rendering.
    - **Interactive Multi-Select**: New `MULTISELECT` visual type rendering as a checkbox list with automatic parameter synchronization.
- **Collapsible Containers**: Support for `COLLAPSABLE = ON`, `ICON`, and pinning logic for overlay drawers and sidebar panels.
- **Deferred Execution**: Added `RUN` button support with staged parameter batching (prevents report refresh on every slicer change).
- **Visibility Engine**: Standardized `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`); added support for dynamic visibility via `@variables`.
- **Enhanced Date Picking**: Native `RELDATEPICKER` (hybrid text + calendar) support.
- **Markdown Tables**: Full support for GFM-style tables in `TEXT` visuals via `marked.js` integration.

**Data, Lineage & Orchestration**
- **Shared Datasets**: Implemented a global dataset registry allowing reports to consume cached, shared data with automated background refreshes and access control.
- **OpenLineage Integration**: Support for exporting data lineage in OpenLineage-compliant JSON format.
- **Lineage 2.0 Engine**: 
    - **Standard Tag Library**: Defined 20 core lineage tags (`@pii`, `@sensitive`, etc.) with `@pii: true-wins` inheritance logic.
    - **Transformation Tracking**: Automated recording of transformation types (`Cast`, `Aggregation`, etc.) across the pipeline.
    - **Visualization**: Enhanced Mermaid-based lineage graphs with distinct shapes for Reports and Datasets.
- **Data Lake Connectors**: Native support for **Snowflake** and **BigQuery**.
- **Batch Separator**: Added `GO` keyword support for separating execution batches.
- **Improved Loops**: `FOR` loops now support implicit start values with `FOR @i TO 10`.
- **QUALIFY Clause**: Added T-SQL/Snowflake-style `QUALIFY` clause for filtering results based on window function values.
- **Window FILTER**: Support for the `FILTER (WHERE ...)` clause inside aggregate window functions.
- **@@FETCH_STATUS**: Added support for checking cursor/foreach fetch status.

**Security & Governance**
- **JWT Secret Generation**: New `GENERATE JWT_SECRET` command for securing report portal communications.
- **Proactive Guardrails**: Linter now warns on high-risk operations and blocks sensitive directory access more aggressively.
- **Decompression**: Added `DECOMPRESS FILE` and `DECOMPRESS DIRECTORY` statements to the specialized operations library.
- **PGP Engine Hardening**: Improved `PGP_KEY_PAIR` generation and validation logic.

**IDE, Tooling & UX**
- **Terminal IDE (TUI) 2.0**: Massive overhaul of the TUI with scrolling, smart copy, message panel optimization, and specialized visual rendering.
- **Unified IntelliSense**: 
    - New dot-aware suggestion engine with priority-based ranking and member-access discovery.
    - LSP support for `@`-prefix tag completions and documentation hovers.
    - Finalized purge of unstable semantic features for improved stability.
- **VS Code Preview**: Support for new chart types (Bubble, Radar, Candlestick, Map) and improved sidebar variable discovery.
- **Report SQL Audit**: Comprehensive rewrite of `Report_SQL_Guide.md` and inline help files to match current production state.
- **Deployment Packaging**: Integrated Windows MSI/ZIP, Linux `.deb`/ZIP, macOS DMG/ZIP, and platform-targeted VSIX generation into the release pipeline.

### Fixed
- **Multi-Select Regression**: Fixed a duplication bug where legacy dropdown logic was overwriting the new checkbox-list implementation.
- **Markdown Rendering**: Resolved issues where Markdown tables were displayed as raw text due to library interface mismatches.
- **IntelliSense Regressions**: Fixed missing connector option suggestions and asterisk expansion failures.
- **Portal State Bugs**: Resolved "white screen" and state synchronization issues in the report portal.
- **Slicer Logic**: Fixed null-reference errors in `renderSlicer` when actions were undefined.
- **Cross-Filesystem Paths**: Fixed portal publish flow failures when handling paths across different drives.
- **Gauge Rendering**: Resolved template string errors and implemented auto-formatting for decimal values.
- **Notebook Reliability**: Fixed "REPL process exited unexpectedly" and communication deadlocks by implementing atomic process lifecycle management and heartbeat checks.
- **Protocol Standardization**: Migrated REPL communication to strict PascalCase JSON with mandatory CRLF endings for Windows pipe stability.

### Changed
- **Sample Reorganization**: Expanded the curated `samples/` library and redirected generated sample outputs under `samples/output/` patterns for repository cleanliness.
- **Visibility Syntax**: Standardized report visibility on the unified `VISIBLE` property.
- **Directory Connections**: Statements like `COPY DIRECTORY` and `FILE_LIST` now natively accept `DIRECTORY` connection aliases as path arguments.
