# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## Versioning Policy

Version numbers follow [Semantic Versioning 2.0.0](https://semver.org/).
- **Pre-1.0.0 (`0.y.z`):** The engine runtime is in active development. Minor version increments (e.g., `v0.13.0` to `v0.14.0`) may introduce breaking changes or syntax deprecations, which are formally cataloged in [BREAKING_CHANGES.md](BREAKING_CHANGES.md). Patch version increments (e.g., `v0.14.1`) are strictly reserved for backwards-compatible bug fixes.
- **Production (`1.0.0` and beyond):** Upon reaching `1.0.0`, the public API, syntax grammar, and execution behaviors are considered stable. Breaking changes will only occur on major version increments (e.g., `v2.0.0`).

---

## [Unreleased]

### Added

- Added `ASSERT JOB <name> (<predicates>) [ON FAILURE ALERT <connection>] [ON CRITICAL_FAILURE THROW]`,
  asserting on the run's own metrics rather than a query result: `ROW_COUNT`, `NULL_PERCENT(<col>)`,
  qualified `NULL_PERCENT(<target>.<col>)`, `FRESHNESS(<col>)`, `QUARANTINE_PERCENT`, and
  `WARN_PERCENT`, each comparable against a literal/interval or against a historical baseline with
  `WITHIN <fraction> OF HISTORICAL`; supported historical metrics also accept
  `WITHIN <n> SIGMA OF HISTORICAL`. Metrics are collected in-stream during the run (never a post-run
  re-scan), so write-only sinks are supported. Historical baselines use the mean of recent completed
  runs and skip themselves below a configurable minimum (`Engine:DataQuality:MinHistoryRuns`,
  default 3; sigma default 10) so new jobs do not alert-storm. Per-column null metrics are persisted
  to job history for target-aware `NULL_PERCENT ... OF HISTORICAL`. Failures can post a counts-only
  summary through a webhook connection — sample data is never included — and optionally fail the run.
  Orchestrator-hosted alerts are transition-based: pass→fail alerts, repeated fail→fail runs are
  suppressed until `Engine:DataQuality:AlertRealertHours` elapses (default 24), and fail→pass sends
  a recovery notification.

- Added column-level data-quality rules: `@expect` / `@fail` tags declared inline on SELECT columns,
  routed by a trailing `ON FAILURE <ACTION> [TO <table>] [WITH (RETENTION = '…')]` clause. Rules
  cover `NOT NULL`, `UNIQUE` (plus `UNIQUE WITH (cols)` and `UNIQUE_FIRST/LAST BY <expr>`),
  `MATCHES <regex>`, `IN (<list>)`, `EXISTS IN table(col)`, `EXPR <predicate>`, and numeric
  comparisons; actions are `THROW`, `WARN` (aggregated diagnostics, optional row capture), and
  `QUARANTINE` (row diverted to a capture table with the `__dq_*` provenance columns). Failing rows
  are captured pre-projection so stewards see the cause, `@pii` values are masked in diagnostics and
  logs, and per-run quarantined/warned counts are persisted to job history and surfaced on the
  execution result. Rules are validated at lint time (malformed rules, non-sink QUARANTINE,
  orphaned clauses, missing section labels) and appear in editor completions.

- Added the first quarantine-remediation v2 foundation: orchestrator-hosted jobs now persist a
  replay manifest when rows are quarantined, recording the job, script path, section label, source
  table, quarantine target, replayability flag, non-replayable reason, and captured input schema
  fingerprint. Single-table labeled quarantines are marked replayable; join-source quarantines are
  captured normally but marked non-replayable until the v3 provenance design lands.

- Added data-quality quarantine disposition enforcement for `UPDATE`: `__dq_*` evidence columns are
  immutable except `__dq_status`, warn rows cannot be released, and quarantine statuses follow the
  v2 lifecycle (`quarantined` may become `released` or `discarded`; `released` may become
  `replayed` or `discarded`).

- Added `REPLAY QUARANTINE <table>` preflight support. The statement resolves the orchestrator
  replay manifest, rejects missing or non-replayable quarantine targets with clear errors, scans for
  rows marked `released`, and returns a ready summary table. Full resume-at-label replay and replay
  leasing remain separate remediation slices.

- Added a write-only `WEBHOOK` connector (aliases `SLACK`, `TEAMS`) that POSTs each inserted row as a
  JSON payload — Slack/Teams message shaping via `FORMAT`, custom bodies via `BODY_TEMPLATE`, and
  opt-in retry policy. The endpoint URL is treated as a credential: `SECRET:` references resolve on
  `URL` for webhook connections, and the URL is masked to scheme + host in `SHOW CONNECTION`, logs,
  and error messages. Every request and redirect hop passes egress-policy validation; only 307/308
  redirects are followed so a delivery is never silently downgraded to a body-less GET.

## [0.17.0] — 2026-07-23

### Added

- Added a design-time script DAG/Flow preview for `.etlsql` and `.rptsql` authoring surfaces, derived
  from parsed script text and wired into existing shared DAG rendering paths.
- Added report-designer ergonomics for keyboard deletion, save shortcuts, escape-to-clear,
  grid nudging, undo/redo, duplication, multi-select movement, container detachment, container
  collapse, tab/accordion child assignment, dynamic column mapping suggestions, and dataset-column
  drag-and-drop mapping.
- Added a business-consumer Portal home experience with favorites, recently viewed reports, featured
  reports, popularity sections, and permission-aware catalog discovery.
- Added fuzzy and synonym-aware Portal catalog search with match reasons across titles,
  descriptions, tags, folders, and report metadata.
- Added self-service report access requests, report-owner/admin approval and denial endpoints, and
  report-level ACLs so approvals can grant one report without broadening folder access.
- Added published-report metadata headers with owner/contact, freshness, last-refresh state, and
  interactive tag badges that navigate or post catalog-search intents.
- Added stale-report refresh requests: users with `Execute` can start a refresh, while read-only
  consumers create an audited owner request without bypassing permissions.
- Added one-click "My Default View" saving for current report parameter/slicer state, updating a
  single per-user default saved view.
- Added `DATE_SUFFIX` and `SUFFIX_SEPARATOR` file-operation options for common dated archive names
  on copy/move flows.
- Extended `SHOW SCHEMA`/`DESCRIBE` lookup so file-based connections can expose schema metadata to
  authors and agents.
- Added `SHOW PROTECTED DATA [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` to inventory protected lineage tagged as PII, PHI, PCI, sensitive, confidential, or restricted from local, Portal, or Orchestrator catalogs.
- Added `SHOW PROTECTED DATA SUGGESTIONS [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` for reviewable classifier findings from column names, source-column names, catalog metadata hints, and supported sampled values without automatically changing tags.
- Added `SHOW PORTAL AUDIT [ACTION '...'] [LIMIT n] [INTO #temp]` for script-first Portal audit review, including steward-impact lineage events.
- Added `samples/08_Reporting/protected_data_audit.rptsql` as a starter protected-data stewardship dashboard.
- Added Portal Lineage Audit mode for a steward-focused workflow that combines protected inventory, classifier suggestions, metadata queues, stale protected assets, inferred impact, steward-impact audit rows, and audit outbox health.
- Added tag-driven governance policy lint and Portal runtime gates for public dataset stewardship metadata, restricted/confidential public datasets, protected dataset exports, and `@quality=gold` promotion metadata.

### Performance

- Reduced Portal catalog-search allocation pressure by replacing the Levenshtein two-dimensional
  allocation with rolling buffers.
- Cached request-scoped Portal group lookups by user to avoid repeated `UserGroups` queries during
  catalog and permission checks.
- Compiled repeated variable-interpolation regular expressions and optimized soft equality byte-array
  comparison with span-based sequence comparison while preserving existing DateTime second-level
  semantics.

### Security

**SFTP host-key verification is now closed by default**
- The SFTP connector previously connected with only a logged warning when `HOST_KEY_FINGERPRINT` was
  unset, trusting whatever server answered. With no trust anchor the client cannot distinguish the
  real server from an interceptor, so an unpinned connection is now **rejected**.
- Added `ALLOW_UNPINNED_HOST_KEY` (default `false`) to opt out explicitly where an unverified
  connection is genuinely intended, making that an intentional choice rather than the default. A
  fingerprint that is set but does not match is still always rejected; the opt-out does not weaken it.
- **Breaking:** scripts using SFTP without `HOST_KEY_FINGERPRINT` now fail until they set either the
  pin (preferred — `ssh-keygen -lf <server_host_key>`) or `ALLOW_UNPINNED_HOST_KEY = 'TRUE'`.
  See [SFTP connector](docs/reference/connectors/services/sftp.md).

**Cached schema reads re-check egress policy**
- The Workstation editor's schema endpoint served table and column names straight from its cache
  without consulting the connector that enforces egress policy, so a host blocked after the cache
  warmed kept being completed in the editor. Policy is now re-checked on every request and a denied
  host returns `403`.
- Report access approval is now report-scoped by default through `ReportAcl` and audited atomically
  with the grant/denial mutation.

### Added

**Stewardship catalog impact analysis**
- Added `/api/catalog/impact` for upstream, downstream, and bidirectional impact analysis by table,
  column, job, script, dataset, report, subscription, owner, and steward.
- Added Portal Lineage Impact mode and pre-publish report validation impact summaries so publishers
  can review affected reports, datasets, subscriptions, jobs, owners, and stewards before changes.
- Added auditable `STEWARD_LINEAGE_IMPACT` hooks for report execution and persisted ad hoc
  interaction lineage changes that affect steward-owned assets.
- Added [Data Stewardship and Impact Analysis](docs/guides/data-stewardship-impact.md) as the
  operator and publisher usage guide.

**Real column types for MOCKDB and SQLite**
- The schema and session explorers previously showed `ANY` for every MOCKDB and SQLite column. Both
  now report real declared types, including nullability and primary keys.
- **Note:** the schema cache is consulted before the connector, so an existing workstation keeps
  showing `ANY` until its cached entry ages out (14-day maximum) or `%LOCALAPPDATA%/ETL-SQL/SchemaCache`
  is cleared.

**Editor CLI rejects unknown options**
- `etl-sql-editor` previously ignored an unrecognised flag and then treated its value as the
  workspace path, so `--profile dev` silently opened a folder named `dev`. Unknown options now fail
  with usage. `--profile` was removed from the documented command shape; local connection profiles
  were deliberately not built.

**Report Designer lays out against the last compiled snapshot**
- The designer canvas now renders visuals using data from the report's most recent `.etlsnap`
  package instead of empty wireframe placeholders, so layout decisions are made against real shapes
  without touching a production database. Rows are capped at 500 per visual and the canvas badges a
  sampled snapshot.
- A report that has never run, or one whose output depends on the viewer's identity, has no shared
  snapshot and continues to show placeholders — identity-sensitive reports deliberately never
  persist one.

**Result grid no longer renders unbounded result sets**
- The grid built one row of DOM for every row returned. Runs started from the Workstation editor and
  the Portal are capped, but the VS Code REPL streams whatever the CLI evaluated, so a large
  `SELECT` could hang the results panel. The grid now draws at most 5,000 rows and labels a
  truncated view "showing first N of M". Export is unaffected and still writes every row.

**`WAITFOR FILE UNLOCKED` no longer reports a false syntax error**
- The linter grammar modelled only `WAITFOR DELAY | TIME | (condition)`, so a valid
  `WAITFOR FILE UNLOCKED` statement was flagged as a syntax error in the editor and completion
  stopped offering next tokens. The parser always accepted it.

### Changed

**Connector assemblies**
- Split the monolithic `ETL-SQL.Connectors` assembly into per-domain projects — `.Cloud` (S3, Azure
  Blob, SharePoint), `.Messaging` (Kafka, SMTP), `.Remote` (FTP, SFTP, Directory, Active Directory)
  and `.Databases` (the ten database connectors, plus `DatabaseConnectionStringBuilder` and
  `ConnectorRetryPolicy`) — alongside the existing `.Common` and `.Files`. Hosts now reference only
  the connector groups they register, so a host no longer drags in every provider SDK transitively.
  Provider namespaces are unchanged, so scripts and connection syntax are unaffected.

**Release gate**
- `Test-PreRelease.ps1` now fails when `THIRD-PARTY-INVENTORY.md` no longer matches the package
  graph, so the licence review and NOTICES cannot silently drift.
- Ten build and publish scripts were renamed from `under_scores` to `hyphens`
  (`publish-release.ps1`, `build-msi.ps1`, `build-linux-packages.sh` and so on). Anything invoking
  them by path needs updating; `scripts/README.md` lists all 90 scripts.

## [0.16.0] — 2026-07-19

### Added

**Central Security Events**
- Added a versioned, vendor-neutral security-event contract with correlated policy denials, lifecycle failures, override attempts, enrollment changes, and resource-limit violations across every host.
- Added a bounded durable local outbox, acknowledgement-based HTTPS delivery using enrolled machine identity, signed-policy severity filtering, bootstrap OS/file sinks, delivery diagnostics, and optional fail-closed health thresholds.
- Added fault-injection coverage for collector and acknowledgement failures, corrupt state, storage pressure, crash recovery, redaction, and enforcement independence from monitoring availability.
- Documented the collector protocol and example Splunk CIM, Elastic ECS, and Microsoft Sentinel ASIM field mappings.
- Added retained-evidence Windows and Linux enterprise certification lanes covering policy lifecycle, enforcement boundaries, standalone behavior, and security-event delivery.
- Certified enterprise policy bootstrap across Portal, Orchestrator, CLI, TUI, Report Player, Report Builder, Language Server, scheduled jobs, spawned runners, and parallel execution; corrected Report Player policy configuration ordering.
- Added retained malicious-input and policy-bypass drills; canonicalized connector aliases before policy enforcement and stripped log-forging characters from security events.
- Certified unenrolled standalone startup with no enterprise HTTP clients or remote event collector, unchanged local configuration, and unrestricted local workflows.

**Schema-resilient flat files**
- Added schema-resilient CSV and Excel ingestion modes: map columns by header, ignore extra source columns, and null missing columns so upstream schema drift no longer fails a load.

**Portal report editor**
- Added an in-designer report preview pane to the standalone script editor so authors can render a report without running a separate serve command.
- Separated **Save** from **Git commit** in the editor, each with its own action, so saving a draft no longer forces a commit.

**Engine**
- Added data-source cancellation hooks so long-running source reads observe cancellation and unwind promptly.

### Changed

- **Connector modularization.** Split the connector implementations into independently deployable projects — `ETL-SQL.Connectors.Common` (shared helpers) and `ETL-SQL.Connectors.Files` — and decoupled `ConnectionStringBuilder` from the database drivers so a host no longer loads every database, cloud, messaging, and native dependency to use one connector.
- **Thinner Portal controllers.** Extracted `ReportScriptInspectionService`, `ReportDependencyService`, and `ReportStructureService` out of `ReportsController`, moving report-parameter parsing, dependency resolution, and structure/AST work into application services.
- Renamed internal `ReportPortal` identities to `Portal` for a consistent namespace.
- **Documentation restructure.** Reorganized the docs tree around single-responsibility sections (`guides/`, `reference/`, `architecture/`, `administration/`, `releases/`) with thin guide hubs and a task index; embedded runtime-help filenames were preserved so in-app help keeps resolving.
- Enforced the documented source-tier layering with architecture boundary tests, so a new upward project reference or banned cross-layer package fails CI.

### Performance

- Bounded Portal storage sampling so usage reporting no longer scans unboundedly on large stores.
- Batched Portal user-role lookups to remove per-user round trips when listing users.

### Security

- Canonicalized connector aliases before policy enforcement and stripped log-forging characters from emitted security events.
- Corrected the Docker security-event outbox startup path so containerized hosts initialize delivery reliably.
- Resolved the security and release findings raised in the v0.16.0 sprint code review.

### Fixed

- Serialized enterprise policy initialization and ignored stale policy notifications so runtime configuration and security-event transport cannot regress during concurrent refreshes; disposed configuration roots now release their policy subscriptions.
- Restored true Release builds for `ETL-SQL.Analysis`, redacted fatal CLI/TUI startup exceptions, and passed report-launch arguments without string concatenation.
- Restored the repository format gate by correcting import ordering in enterprise security and fleet-policy files.
- Propagated cancellation through warehouse and data-source schema resolution so cancelled jobs stop promptly instead of completing schema work.
- Read Portal user lists using the paged API so large directories return complete, correct results.
- Included the split connector projects in the Docker restore so container builds resolve every connector assembly.

## [0.15.0] — 2026-07-12

### Added

**SQL Logic, Parser & Correctness Fuzzing (Phases 1-4 & Hardening)**
- Shipped pure in-memory `MOCKDB()` crash-testing fuzz harness, executing up to 1,000,000 queries in under 5 minutes without memory leaks or unhandled parser faults.
- Added **NoREC (No Relation Query Evaluation)** correctness checks, automatically comparing optimized count queries against unoptimized case-when sum queries on `MOCKDB()` to assert logical execution parity.
- Added **Token Corruption & Mutation Fuzzing** (5% probability) to ensure the parser recovers cleanly with structured `SyntaxException` warnings rather than unhandled index or reference crashes.
- Extended fuzzer query walks to support advanced relational syntax: windowing functions (`ROW_NUMBER()`, inline partition/order frames, and named `WINDOW` declarations), filtering clauses (`QUALIFY` and aggregate `FILTER(WHERE...)` clauses), and advanced grouping set combinations (`ROLLUP`, `CUBE`, `GROUPING SETS`, `ALL`).
- Added diagnostics, concurrency blocks (`PARALLEL BEGIN ... END`), transactional bounds (`COMMIT`/`ROLLBACK`), system options (`SHOW`/`SET`), and global variables (`@@NOW`, `@@TODAY`).
- Integrated recursive AST expression minimizer (`QueryMinimizer.cs`) to isolate and prune crashing queries to minimal reproduction cases.
- Configured fuzzer iterations using the `ETLSQL_FUZZ_ITERATIONS` environment variable, defaulting to 500 for check-ins.

**Column-to-Column Interactive Lineage Engine**
- Built an interactive, high-fidelity Vanilla JS column-to-column lineage graph engine featuring ReactFlow-style visuals, visual mapping ports, midpoint edge badges, and column path isolation.
- Added cursor-pinned zoom math, floating details sidebar, Ctrl-Click lineage filtering, node filters, PII toggles, inline formulas, and recursive BFS column lineage traces.

**Shared Connection Governance & Secret Hardening (Phase 7)**
- Added organization-designated sensitive connection metadata and per-connection use ACLs.
- Added connection catalog with `SHARED:alias` expansion.
- Shipped Portal secret store (admin API, provider, key-ring checks).
- Created native admin services (Slice E) and lifecycle CLIs (Slice A) for secrets.
- Hardened parsing to reject unquoted `SECRET:` or `ENC:` values and lint unresolvable references.

**High Availability (HA) & Soak Certification (Phase 6)**
- Added native HA large-job soak runner, HA fault-injection runner, and CLI commands.
- Integrated HA diagnostics bundle and metrics snapshots.
- Shipped sustained load workload templates, topology harness, and evidence validation gates for pre-release verification.

**Adaptive Execution & Resource Controller (Phase 2)**
- Integrated adaptive worker admission and concurrency caps for parallel loops.
- Wired adaptive batch and memory grant setpoints based on resource sampler.
- Gated spill writes with adaptive concurrency.

**Allocation Budgets & Spill Churn Reduction (Phase 1)**
- Met Gate F round-trip performance benchmarks: +74% throughput, -63% GC allocations at scale (10M / 50M rows and 1B scale certification).

## [0.14.0] — 2026-07-05

### Added

**Enterprise Policy Enforcement & Monitoring (Phase 3)**
- Added an administrator-only policy-authority API (`api/admin/policy-authority`) to validate, version, sign, publish (staged or active), activate a staged version, emergency-rollback, and retrieve organization policies per tenant/environment, backed by a durable append-only published-version history (dual-provider SQLite/PostgreSQL migrations).
- Added machine-authenticated policy distribution (`GET api/policy-authority/envelope`): enrolled machines retrieve their signed policy using enrollment headers plus an optional TLS client certificate; responses are bound to the registered tenant/environment, and unknown, revoked, or reassigned machine identities are refused and audited.
- Added a policy-authority availability health check and signing-key-rotation tracking; publication, activation, rollback, machine revocation, and distribution denials are recorded in the durable audit trail.
- Added staged rollout and emergency rollback with monotonic issuance, so clients that reject older issuance times always converge on the newer signed version.

**Billion-Row Columnar Execution Foundations**
- Designed and implemented a native, high-performance, append-only segmented `#temp` storage engine with `ColumnBatch` buffers to bypass row-at-a-time (`Row`/`DataTable`) overhead.
- Built a process-wide memory-grant arbiter (RAM governor) backing external sort, join, distinct, aggregates, and window query operations, dynamically controlling memory ceilings and triggering partition spilling.
- Optimized spilling to use large sequential spill extents (128 MB target) to reduce file metadata and reader/writer overhead.
- Integrated bounded double-buffered pipelining to overlap extent writing with chunk production.
- Optimized projection, UTF-8 selection slicing, and key-only/numeric aggregations directly on native buffers (columnar islands).
- Added adaptive hash partition sizing, window/join fan-out scaling, and sort run extraction without boxing.
- Integrated scale certification tiers: Smoke (1 GB), Standard (4 GB, 10M rows), Stress (8 GB, 5M rows), and Huge (16 GB, 50M rows).

**Row-Level Security (RLS) & Impersonation (Phase 1 & 2)**
- Added identity system variables (`@@CURRENT_USER`, `@@CURRENT_USER_ID`, `@@REAL_USER`, `@@IS_ADMIN`) and functions/predicates `HAS_GROUP('name')` / `HAS_ROLE('name')` with default-on admin bypass.
- Added table-valued `USER_GROUPS()` and `USER_ROLES()` to query active groups/roles in joins.
- Implemented secure preview-as/impersonation for folder editors and administrators, never-cached sensitive reports, and recipient-level execution identity resolution for subscription emails.

**File Connectors & Excel Write Support**
- Added write and append support for Excel (.xlsx) files via MiniExcel.
- Enforced stream-on-the-fly decryption and decompression for FlatFile, JSON, XML, and Excel connectors.
- Added support for `.etlds` extension for exported dataset files and `.etlsnap` for Apache Arrow snapshots.

**Host Metrics & Operational Alerting**
- Added persistent host metrics tracking disk/memory/CPU capacity, a new `SHOW HOST METRICS` statement, and daily rollups.
- Added automatic reconciliation of stale RUNNING jobs as `INTERRUPTED` on startup.
- Shipped Portal operational metrics digest email.

**SFTP Connector Hardening**
- Host-key verification using `HOST_KEY_FINGERPRINT` for MITM protection.
- Opt-in atomic upload (`ATOMIC_UPLOAD = true`) uploading to temporary files before renaming.

### Changed

- **Octocolee Product Naming:** Introduced Octocolee as the product name (ETL-SQL remains the engine name).
- **Default Columnar Temp Storage:** Configured columnar temp storage by default.
- **Release Infrastructure:** Added lightweight secret scan, SBOM generation, and pre-release gates.

### Fixed

- **Parser and Security Fixes:** Sanitized `QuoteIdentifier` routines to prevent SQL injection.
- **VS Code Extension & TUI Fixes:** Fixed VS Code extension vulnerabilities, terminal command builder escape bugs, and resolved window resize lag/input blocking on Unix in the TUI.

### Security

- **Execution Policy Enforcement Boundary:** Added execution policy snapshot context (`ExecutionPolicySnapshot`) and dynamic policy validation.
- **Shared Enforcement Snapshot:** An immutable policy snapshot is captured when execution begins and propagated unchanged through CLI, TUI, Report Player, Portal, Orchestrator, parallel branches, recursion, and scheduled jobs, making denials deterministic across in-process and spawned execution.
- **Governed Connector Egress:** Enforced enterprise connector-type, destination host, scheme, and port allowlists before DNS resolution and connection creation, including dynamic REST redirect/pagination/template targets. Local egress denials surface as a plain security error; organization-policy denials carry the governed key and correlation identity.
- **DNS-Rebinding & Proxy-Bypass Hardening:** The REST connector re-validates the DNS-resolved address at connect time and pins the socket to the validated set, and disables ambient proxy use — closing rebind-to-internal-IP and proxy-bypass paths. Obfuscated IP literals are normalized and loopback/link-local/private/CGNAT/ULA ranges are denied unless explicitly listed; URL-embedded credentials are rejected regardless of policy.
- **Filesystem Policy Boundary:** Restricted local paths in remote file transfers, directory synchronization, and recursive file/directory operations. `COPY FILE` and recursive directory copy stream through handle-validated opens (OS-resolved final-path re-check after open) to resist link-substitution races; delete/move/copy re-authorize immediately before the OS call.
- **Governed Resource Ceilings:** `MAX_PARALLEL_DEGREE`, `MAX_FILE_OPERATIONS`, `MAX_RECURSIVE_DEPTH`, `MAX_SMTP_EMAILS_PER_SCRIPT`, and `MAX_STRING_RESULT_SIZE` cannot be weakened by `SET`, configuration, environment variables, command-line options, restored sessions, or report parameters; the enterprise ceiling is bound from the immutable execution snapshot at execution start and re-checked at each operation boundary.
- **Allowed Extension Tightening:** Removed generic `.tmp` from whitelisted user file extensions to prevent insecure temp file usage.

## [0.13.0] — 2026-06-28

### Added

**Apache Arrow Snapshot Integration**
- Completed end-to-end Apache Arrow IPC snapshot support: the `SnapshotStore` now saves and loads secure `.etlsnap` zip packages by default in CLI and local execution contexts.
- Local and CLI snapshot packaging runs without explicit key configuration by falling back to host-bound at-rest encryption (see Security for the hardened behavior).
- The report runtime player now lazy-loads and decodes Arrow IPC streams on-demand with automatic fallback to JSON row endpoints for older clients.
- Downloaded and bundled the minified Apache Arrow JS library (`arrow.min.js`); synchronized front-end runtime assets across Portal, Player, and VS Code extension.
- Added test coverage verifying CLI/local `.etlsnap` roundtrip packaging.

**Portal Execution Metrics & Observability**
- Added persistence of per-execution resource metrics (CPU, memory, duration) to the Portal database so historical load can be trended over time (`AddPortalExecutionResourceMetrics` EF migration for both SQLite and PostgreSQL).
- Exposed a historical execution load metrics endpoint on `AdminController` for operators and monitoring systems.
- Added lazy-loading of Arrow snapshot rows in the Portal to avoid pulling large result payloads into memory until requested.

**`SHOW PORTAL USAGE METRICS` and `SHOW PORTAL OPERATIONAL METRICS` Statements**
- Added `SHOW PORTAL USAGE METRICS [INTO #t]` inside an `EXECUTE portal` block to return report view counts, unique viewers, refresh health, and subscription delivery failures for the requested period.
- Added `SHOW PORTAL OPERATIONAL METRICS [INTO #t]` to return live queue depth, execution concurrency caps, recent failure counts, storage size, schema migration status, and last-24-hour execution load/resource buckets — complementing the existing `GET /health` endpoint with a scriptable, queryable form.
- Wired both statements through the parser (`SystemParser`), AST (`ShowPortalUsageMetricsStatement`, `ShowPortalOperationalMetricsStatement`), and `PortalDataSource`; updated `PORTAL_SHOW.md` help file, `Grammar.md`, and `Syntax_Index.md`.

**`SHOW LOCKS` Statement**
- Added `SHOW LOCKS` to display currently held engine-level and orchestrator-level resource locks, aiding live diagnosis of stalled pipelines and contention scenarios.
- Documented `SHOW LOCKS` in `Grammar.md`, `Syntax_Index.md`, `User_Manual.md`, `PORTAL_SHOW.md` help file, and the `SHOW` keyword help document; wired a corresponding test in `SystemAndReportHandlerTests`.

**LSP Cross-File Declaration Resolution**
- Extended the Language Server's `DefinitionProvider` and `HoverProvider` to resolve `GO TO DEFINITION` and hover targets across all currently open files in the workspace, not just the active document.

### Changed

**Performance — Engine & Language Server**
- Indexed lineage in `LineageTracker` and cached parameter scans in `ParameterScanner` to avoid repeated linear walks during analysis and execution.
- Added parse-result caching to `RunScriptStatementHandler` so `RUN SCRIPT` targets that have not changed on disk are not re-parsed on every invocation.
- Cached LSP definition declarations in `DefinitionProvider` and `DocumentStateStore` to avoid redundant re-analysis on every keystroke.
- Hardened Portal metrics and scaled hot paths: added `AssetFingerprinter`, tuned spill-store and external sort/join engines, and improved scheduler throughput under load.

**Machine-Aware Orchestrator Throttling & Startup Sweep**
- `JobThrottle` now reads available logical processors and physical memory at startup to derive a machine-aware default concurrency ceiling, preventing over-subscription on small VMs.
- Added `ChildProcessTracker` to associate child processes spawned by the Orchestrator with their parent job, enabling clean resource reclamation on job cancellation.
- Added a startup temp-table sweep in `EngineRunner` to remove orphaned `#temp` working directories left by crashed sessions, preventing unbounded disk growth.

**Stabilization & Refactoring (Engine, Analysis, Portal, TUI, Tooling)**
- Completed a broad stabilization pass across the engine: audited and hardened all `ETL-SQL.Engine` statement handlers, `RelDateResolver`, `ResultFormatter`, `SessionStateManager`, `VariableScopeManager`, `CteManager`, `PushdownEngine`, `QueryCompiler`, `DataSourceManager`, `LineageManager`, and `SpillStore`.
- Hardened the `AliasScanner`, `SnippetLibrary`, and `SnapshotStore` in `ETL-SQL.Core` and `ETL-SQL.Reporting`; made the `sync-assets.js` asset-sync script idempotent and banner-aware.
- Tightened `AbsolutePathRule`, `CredentialLeakRule`, and `FileSystemSecurityRule` linting rules with additional corpus cases for path boundary and credential-leak scenarios; strengthened `SchemaValidationRule` in Analysis.
- Hardened `CryptoUtils`, `MachineBoundCrypto`, and `LruCache` in `ETL-SQL.Core.Common`; hardened `SqliteSessionMetadataStore` with retry semantics and tighter WAL mode configuration.
- Hardened engine cleanup and path handling across `RunScriptStatementHandler`, `ExecuteStatementHandler`, `BundleStatementHandlers`, `WaitForFileStatementHandler`, `CteManager`, `ProcedureExecutor`, and `SessionStateManager`.
- Hardened async export and backup paths in `BackupRestoreService`, `EngineRunner`, `BrowserReportPdfExporter`, `ExportController`, and the TUI `ConsoleEditor`.
- Added `AssetFingerprinter` to the Portal for cache-busting on static asset updates; added EF migration for PII column encryption on both SQLite and PostgreSQL providers.
- Stabilized `JobApiEndpoints` with improved cancellation propagation and error surfacing; tightened `NodeCapacityMonitor` assertions and added `SchedulerService` queue-wait-time argument fixes.

**TUI Frame Metadata Caching**
- `EditorRenderer` now caches rendered frame metadata between redraws, reducing CPU usage during idle periods and making the status bar and key-binding overlays allocation-free on unchanged frames.

**Documentation & Policy**
- Reconciled identity configuration reference in `Administrators_Guide.md` to match shipped OIDC behavior.
- Tightened contribution rules and compatibility policies in `CONTRIBUTING.md`.
- Documented future performance and scalability enhancements in `TODO.md`.

### Fixed

- **Support bundle redaction**: `SupportBundleBuilder` now redacts connection-string passwords, API keys, and JWT secrets from all diagnostic fields before archiving; added corresponding `OperatorToolingTests` coverage.
- **Portal database migration test failures**: Resolved a portal database upgrade migration ordering issue and fixed a metric timezone normalization bug that caused flaky test failures under certain locale configurations.
- **SFTP connector `ConnectionStringBuilder`**: Corrected option serialization for `SFTP` connector key-file auth paths.
- **TUI frame caching**: Fixed stale frame metadata being rendered after connection or tab changes in `EditorRenderer` and `StatusBar`.
- **Migration lint corpus**: Added a migration lint corpus (`test(compat)`) to catch invalid dialect usage introduced across schema migration scripts.
- **Scheduler test mock**: Fixed `SchedulerService` test mocks that passed an incorrect argument count for the queue-wait-time parameter after an API change.
- **GROUP BY ALL column expansion**: Resolved a bug in `SelectStatementHandler` where `GroupByAll` was expanded before output column expansion, resulting in engine crashes when star-modifiers (`* EXCLUDE (...)`) or qualified stars (`t.*`) were present in the query.
- **Positional reference star projection checks**: Hardened positional reference checks in `Parser.ResolvePositionalReference` to correctly identify and block qualified star and star-modifier projections from bypassing positional sorting/grouping syntax checks.

### Security

- **PII column encryption at rest**: Portal database columns storing user PII (email addresses, display names in audit records) are now encrypted at rest using a key derived from the configured Data Protection key ring, applied via a background maintenance service and corresponding EF Core migration for both SQLite and PostgreSQL.
- **Support bundle hardening**: Connection strings, JWT secrets, and API keys are now actively redacted from the support bundle rather than relying solely on config-key exclusion lists.
- **Crypto hardening**: Strengthened `MachineBoundCrypto` key derivation and `CryptoUtils` authenticated-encryption paths; added additional test coverage for encrypt/decrypt roundtrips and tamper-detection.
- **Service Account token exchange timing mitigation**: Hardened the service-credentials token endpoint against client-ID enumeration timing attacks by always executing password verification against a dummy hash when the Client ID is not found or is inactive.
- **Client certificate store handle leak cleanup**: Resolved an OS handle leak in `EnterprisePolicyRuntime` during OIDC/HTTPS policy certificate store searches by disposing non-matching certificate instances.
- **Egress sanitization & parameter utility ReDoS hardening**: Hardened regular expressions in `ConnectorExceptionWrapper` and `ParameterUtility` to use source-generated regex `[GeneratedRegex]` with a `1000ms` timeout to protect against catastrophic backtracking.
- **Snapshot at-rest encryption fallback hardening**: When `Portal:Dataset:AtRestKey` is unset, report snapshot (`.etlsnap`) packages now fall back to the same host-bound `ENCRYPT=MACHINE` protection used for dataset caches (DPAPI LocalMachine on Windows; authenticated AES-256-GCM keyed from the machine id elsewhere), instead of a source-public default key. Reading a key-managed snapshot now fails closed if the key is absent. `MachineBoundCrypto.Protect/Unprotect` are exposed for reuse, and a one-time warning is logged when the host-bound fallback is in effect.
- **Authenticated machine-bound generic encryption**: `CryptoUtils` machine-key protection on platforms without DPAPI is now encrypt-then-MAC (HKDF-SHA256 encryption/MAC sub-keys + HMAC-SHA256 verified in constant time) instead of unauthenticated AES-CBC; legacy CBC-only payloads remain readable.
- **`machine.key` permissions**: the generated machine key file is now created owner read/write only (`0600`, directory `0700`) on Unix, atomically, so it is never briefly world-readable.

## [0.12.0] — 2026-06-19

### Added

**Practical High Availability — Multi-Node Portal & Orchestrator**
- Made both the Portal (EF Core) and Orchestrator (hand-written) state stores **provider-selectable** between SQLite (default, unchanged) and PostgreSQL via configuration (`Portal:Database` / `Orchestrator:Database` Provider + ConnectionString), removing the previously hardcoded SQLite coupling. PostgreSQL is implemented end to end for both stores and verified against a real Postgres via Testcontainers: the Portal gained a dedicated migrations assembly for Postgres, and the Orchestrator store became a provider-neutral `RelationalJobHistoryStore` behind a dialect (portable SQL, with a Postgres `nocase` ICU collation backing `COLLATE NOCASE`).
- Added `etl-sql admin migrate-database --from sqlite --to postgres [--dry-run]` to copy existing single-node SQLite Portal/Orchestrator state into the configured PostgreSQL deployment: values are coerced to each target column's type, foreign-key ordering is bypassed for the load, identity sequences are resynced, and per-table row counts are verified — any mismatch fails closed (nothing is committed). `--dry-run` verifies counts and target-schema compatibility without writing.
- Added a unified `IArtifactStorage` interface with **Local** and **SMB/UNC** providers so reports, scripts, snapshots, and custom-map assets live on a shared root reachable by all nodes, with `SecurityService` guardrails enforced at the storage boundary.
- Added database-backed cluster coordination: **node heartbeats and a cluster registry** (liveness on the database clock, with expired rows pruned on the heartbeat loop), **monotonic fencing tokens** for state and shared-storage writes, and **database-backed leader election** that serializes migrations and singleton work. Stale writers are fenced and in-flight portal work is cancelled on node lease loss.
- Added per-node capacity gating with **job quarantine**, cross-node capacity claims, and snapshot write-failure recovery.
- Added a scalable **HAProxy** docker-compose with sticky (session-affinity) balancing, a configurable shared Data Protection key ring, and a lightweight `GET /healthz` load-balancer probe (richer diagnostics remain on `GET /health`). HA clusters require a shared artifact root, a shared key ring, identical JWT/orchestrator/dataset keys across nodes, and load-balancer session affinity for node-local interactive sessions.

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

### Fixed

**Test Stability**
- Stabilized two timing-sensitive Docker integration-lane tests that failed intermittently only under full pre-release load: relaxed a `Retry-After` delay assertion to tolerate the ~15.6ms Windows timer quantum, and raised the orchestrator scheduled-job history poll timeout above the container's own job timeout so a job nearing its budget under load is not abandoned prematurely.

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
- Added [PipelineGenerator](./src/ETL-SQL.App/App/PipelineGenerator.cs#L14) and [SpecExtractor](./src/ETL-SQL.App/App/SpecExtractor.cs#L12) test suites under `tests/ETL-SQL.Tests/App/` covering contract validation, generated-script parsing, review metadata, validation gates, and PDF trimming scoring.
- *Note on limits*: This is a developer productivity feature, not an automated production-pipeline generator. LLM spec parsing and vendor formats are variable; generated scripts are intended as reviewed starting points. Developers must verify the JSON, complete the extraction query, review evidence/low-confidence fields, and test against real vendor files.

**Terminal IDE (TUI) Modernization**
- Implemented collapsible sidebar file explorer tree and tabbed multi-file support in [ConsoleEditor.cs](./src/ETL-SQL.TUI/UI/ConsoleEditor.cs#L29).
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
- Added a native **Neo4j** graph database connector supporting key merging, validation, and metadata queries (see [Neo4jConnector.cs](./src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jConnector.cs) and [Neo4jDataSource.cs](./src/ETL-SQL.Connectors.Databases/Neo4j/Neo4jDataSource.cs)).
- Added outbound writing support and completed production gaps for the REST API connector.
- Enhanced Azure Blob, SFTP, S3, and local Directory connectors to include fallback decryption and structured path parsing.

**Language, Lineage & Governance**
- Added `CREATE TAG` and `CREATE LINEAGE FROM ...` syntax to support programmatic importing of curated lineage assets and metadata tags.
- Added the `DIFFERENCE(s1, s2)` Soundex similarity scoring string function (see [FuzzyFunctions.cs](./src/ETL-SQL.Engine/Functions/FuzzyFunctions.cs)).
- Added a cross-platform CLI `etl-sql purge` command for cleaning up old data and session histories.
- Expanded SQL Logic Test (SLT) coverage for index creation, table truncation, table alteration, `LEFT SEMI`/`LEFT ANTI` joins, and `QUALIFY` statements.

**Verification & Orchestration Hardening**
- Added job scheduler chaos coverage and concurrency race verification tests (scheduler, subscription, and active-work).
- Added a subscription delivery diagnostics UI and preserved subscription failures in the history store.
- Added verification tests for Report Portal user permission models and user workflows.
- Added a new capacity planning guide (`docs/architecture/roadmaps/Capacity_Planning.md` or similar) and published service capacity baselines.
- Added capacity workload templates and row-volume capacity planning profiles.
- Added scaling tests for portal administration catalogs and enterprise identity lifecycle verification.

### Fixed
- **Query Parser:** Fixed parser bugs for `LEFT SEMI`/`LEFT ANTI` joins and tolerated trailing semicolons (`;`) for statements inside `BEGIN`/`TRY` blocks.
- **Cookbook Recipes:** Audited and fixed all 23 Cookbook recipes to ensure they compile and parse cleanly, fixing issues with `ENCRYPT`, `SEND EMAIL`, `EXEC`, `DECLARE`, and deprecated `WITH PARAMETERS` report options.
- **TUI Editor:** Implemented file overwrite warnings when a file changes on disk, fixed sidebar layout wipeout during redraw by clearing partial line width, and resolved keyboard input lockups on Windows.
- **TUI Autocomplete:** Fixed snippet triggers (`$mssql`) showing inside the autocomplete suggestions and prevented crashes when brackets appeared in prompt titles.
- **TUI Metadata:** Restored temp table querying inside [TuiMetadataManager](./src/ETL-SQL.TUI/UI/SuggestionProviders.cs#L106).
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
- `docs/architecture/roadmaps/Release_Capability_Matrix.md`: release claim matrix tying public product claims to concrete evidence and preventing release notes from overstating tested behavior.
- `scripts/Get-TestLaneInventory.ps1`: static lane inventory report showing discovered xUnit tests by lane, category trait, project, and fast-lane exclusion reason.
- `perf` lane now runs engine hardening performance tests plus the dedicated perf project; `fast`, `portal`, and `full` lanes include the Node lineage UI smoke test.
- Scale certification baselines committed: `certification-results/baseline-smoke.json` (Smoke, 1×) and `certification-results/baseline-standard.json` (Standard, 10×, 13 scenarios, all passing).
- `.github/CODEOWNERS` and Dependabot configuration added.
- Four GitHub workflow templates under `.github/workflow-templates/` (local-validated-release, manual-docker-certification, manual-release-validation, manual-scale-certification) — staged for future activation; not yet wired to automatic triggers.
- `docs/architecture/roadmaps/Release_Workflows.md` documents the local-first release ownership model and workflow template activation guide.
- Windows release packaging scripts hardened for reliable local/CI builds: resolved WiX tool lookup, WiX 3.x Program Files discovery, explicit MSI failure handling, and local validated release workflow WiX installation.

**Documentation**
- `Docs/Architecture/Lineage.md` (new): what is tracked, `LineageEntry` data model, `SHOW LINEAGE` syntax variants, Mermaid and OpenLineage export, `SHOW LINEAGE HISTORY` cross-run catalog, metadata inheritance rules, and Orchestrator (`etlsql.db`) integration.
- `Docs/Reference/Performance.md` (new): see Observability above.
- `docs/architecture/roadmaps/Release_Workflows.md` (new): see Release Infrastructure above.
- Architecture documentation expanded for connector, engine, expression evaluation, language server, lineage, orchestrator, parser/lexer, portal UI, report portal, reporting, TUI editor, variable scoping, and VS Code extension boundaries.
- `docs/guides/testing.md`, `docs/architecture/roadmaps/Test_Strategy.md`, and `scripts/README.md` reorganized around the current lane model, pre-release phases, SLT usage, coverage expectations, and installer prerequisites.
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
- Scale certification claims page added (`docs/architecture/standards/ScaleCertification.md`).
- SLT corpus coverage documented in `docs/architecture/standards/SLT_Coverage.md`.

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

## [Unofficial 0.6.0] — 2026-05-11

### Added

- **Hierarchical Drill-Down and Drill-Through:** Implemented `DRILL_IN` and `DRILL_DOWN` (supporting multi-key drill parameters) for interactive, in-place dashboard exploration.
- **Power BI-style Cross-Visual Highlights:** Added cross-visual highlight filtering with dual-direction updates and dimming/ghosting effects for chart visuals (Line, Scatter, Pie, Donut).
- **Shared Dataset Management:** Built dataset explorer features including persistence, cross-report consumption, access control, LS dataset awareness, and portal-triggered refreshes with async execution.
- **Advanced Parameter & Execution Controls:** Added textbox, numberbox, checkbox scalar inputs, and deferred execution support (RUN button) with staged parameter batching.
- **New Visual Enhancements:** Added collapsible containers, standard `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`), and support for custom GeoJSON maps (`MAP_FILE`) with build-time validation.
- **Interactive Tooling:** Added `serve` command and dynamic `ReportPlayer` lifecycle management for live report previews in-browser.
- **OpenLineage Integration:** Added OpenLineage export support and database catalog metadata imports.

### Changed

- **Sample Reorganization:** Cleaned up and renamed all sample scripts, redirecting outputs to standard `samples/output/` patterns.

### Fixed

- **Portal Reactivity:** Stabilized slicer reactivity, multiselect visual components, and cross-filesystem path handling during portal publishing.

## [Unofficial 0.5.0] — 2026-05-04

### Added

- **Report Portal Subsystem (Phases 1–6):** Introduced the `ETL-SQL.Portal` web application. Features include JWT authentication, role-based access control (RBAC), folder structure organization, report publishing, execution/snapshot tracking, and web-based ECharts/Markdown rendering.
- **Automated Report Subscriptions:** Shipped report subscriptions allowing scheduled report exports via `EXPORT REPORT` sent as Link or Markdown emails, complete with SMTP connection management.
- **Portal Observability & Administration:** Added a `/health` endpoint with JSON diagnostics of database and orchestrator status, audit logs CSV exports, and administrative endpoints.
- **Portal Security Hardening:** Implemented JWT secret validation on startup via hosted service, a path traversal guard, and HSTS security configurations.
- **Apache Arrow Spill Format & Decryption:** Integrated Apache Arrow IPC spill format for high-speed serialized temp table caching, and implemented client-side credential auto-decryption.
- **Unified IntelliSense Engine:** Built a priority-based suggestion ranking, dot-notation autocomplete prefix filtering, dynamic option discovery, and member-access resolution.
- **Data Lake Connectors:** Native support for **Snowflake** and **BigQuery** databases.
- **Security & Encryption:** Added `GENERATE JWT_SECRET` for secure Report Portal communications.
- **Language Syntax Additions:** Implemented `QUALIFY` clause filtering, window function `FILTER (WHERE ...)` support, cursor status checks (`@@FETCH_STATUS`), and `FOR` loop syntax support for implicit start values.
- **TUI IDE Completion:** Overhauled TUI console with path completion, Smart Copy, screen stability, Compare Mode, SHOW commands, and a two-line status bar.
- **Installer & Packaging Release Pipelines:** Integrated MSI, Linux `.deb`, and macOS DMG installer packages with install bootstrap configurations.

### Changed

- **Security Auditing:** Standardized security overrides by migrating legacy comments to formal `SET ALLOW_... ON/OFF` statements.

### Fixed

- **TUI & Telemetry bugs:** Resolved rendering artifacts, status bar layout errors, and stabilized TUI telemetry.
- **LSP Cleanup:** Purged experimental unstable features (Quick Fixes, Smart Rename) for stability.

## [Unofficial 0.4.0] — 2026-04-20

### Added

- **Report-SQL Scripting and `CREATE VISUAL` Support (Phases 9A–9D):** Introduced native support for Report-SQL scripts (`.rptsql`) with `CREATE VISUAL`, `CREATE PAGE`, and `CREATE DATASET` statements. Added full grammar for visual types (BAR, LINE, PIE, SCATTER, TABLE, CARD, SLICER), axes, column mappings, and page slot layout definitions.
- **ReportBuilder Library and CLI Tooling:** Created `ETL-SQL.ReportBuilder` for Chart.js rendering, GFM markdown generation, and snapshot serialization. Shipped the report builder command-line utility with build, refresh, and serve commands.
- **VS Code Extension Preview Integration:** Added a WebviewPanel to the VS Code extension for live report previews, displaying rendered Chart.js charts, tables, cards, and interactive slicers.
- **ReportPlayer Web Dashboard:** Shipped a Kestrel-hosted local dashboard server (`ReportPlayer`) supporting live parameter injection, interactive updates, and auto-refresh endpoints.
- **Orchestration & Scale Hardening:** Implemented job retry logic with exponential backoff and session persistence in the Orchestrator, alongside `#temp` table spill-to-disk and result capping logic.
- **Hyper-scale Window Spilling:** Added deep-spilling mechanism for window query execution to partition results under high-volume workloads.
- **ANSI SQL Functions & Statistical Aggregates:** Implemented standard ANSI string functions (`SUBSTRING`, `POSITION`, `OVERLAY`, `TRIM`, `EXTRACT`), date arithmetic enhancements, and statistical aggregate calculations.
- **Script Assertions:** Added the `ASSERT` statement to natively validate data qualities and script outcomes.
- **JSON & XML Security Hardening:** Replaced bare catch blocks with explicit system exception filters and added security sandbox protections for remote file transfers.
- **LSP & UI Enhancements:** Modernized results panel, TUI performance dashboard, and stabilized telemetry pipelines.
- **PIVOT & UNPIVOT Validation:** Added linter validation for PIVOT columns, quarter-based `DATEPART` support, and query metadata derivations.

### Fixed

- **SMTP Attachment Leak:** Fixed a handle leak for SMTP attachments.
- **3VL Null Handling:** Implemented three-valued logic (3VL) null propagation and fixed substring start index boundary behaviors.

## [Unofficial 0.3.0] — 2026-04-06

### Added

- **VS Code Extension v0.1 Alpha:** Integrated LSP parser with formatting, lineage hover, and smart CLI execution.
- **Security & Encryption Utilities:** Added SSH key pairing (`GENERATE SSH_KEY_PAIR`), connection altering (`ALTER CONNECTION`), and file encryption/decryption (`ENCRYPT FILE`, `DECRYPT FILE`).
- **Serilog Logging Infrastructure:** Integrated Serilog for application-wide logging and consolidated logs to the `logs/` directory.
- **Join Optimization:** Implemented `CompoundKey` to optimize hash joins and handle mixed-type comparisons (string/numeric/date) across diverse sources.
- **Bulk Insert Lineage:** Added explicit column mapping support and column-level lineage tracking.
- **SQL Pushdown:** Enabled SQL pushdown execution and support for standalone `EXECUTE INTO #temp`.
- **Syntax Enhancements:** Supported `LIKE ESCAPE` and grouping sets (`ROLLUP` / `CUBE`).

### Changed

- **Syntax Standardization:** Migrated `ON FILE` to `ON FLATFILE` for file connections.

### Fixed

- **Thread Safety:** Eliminated deadlocks and silent exception swallowing under concurrent execution contexts.

## [Unofficial 0.2.0] — 2026-03-23

### Added

- **Core Query Dialect & Standard Library:** Support for `DISTINCT`, `TOP`, `LIMIT`, `MERGE`, `OFFSET`, `NTILE`, `STRING_AGG`, and transactional statements (`COMMIT`, `ROLLBACK`, `THROW`).
- **Database Connectors:** Added initial support for MSSQL, Postgres, and Oracle database engines.
- **File Connectors:** Read/write capabilities for XML and JSON files.
- **Temp Tables & Indexes:** Support for `#temp` tables with query plan indexes (`CREATE INDEX`) and query plan tracing via `EXPLAIN`.
- **Control Flow & Parallel Execution:** Parallel execution pipelines (`PARALLEL`), cross-script execution (`RUN SCRIPT`), and directory synchronization tasks.
- **Notifications & Transfer Connectors:** Added `SEND EMAIL` and file transfer connectors (SFTP/SSH, FTP, Azure Blob).
- **Linter & UI Foundations:** Added a command-line script editor, local test harness (`--test`), and baseline security linter.


## [Unofficial 0.1.0] — 2026-03-13

### Added

- **Proof of Concept Completed:** Successfully loaded flat files (CSV) and joined them into in-memory `#temp` tables.
- **Abstract Syntax Tree (AST) Parser:** Implemented the initial AST parser to parse SQL statements and evaluate expression trees.
- **Core SQL Execution Engine:** Developed the core engine to execute queries, process DML scripts, and return formatted results.
- **Terminal IDE (TUI) Foundations:** Added a basic console editor interface to write scripts and display execution output.
- **Git Repository Initialized:** Initialized the git repository and established the project structure.
- **Development Kickoff:** Work began on March 6, 2026, to design and prototype the initial engine proof of concept.
