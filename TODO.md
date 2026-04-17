# ETL-SQL Development Roadmap
## TUI on-going issues
- [ ] **Prevent scroll up past window?**  I'm wondering if we start with a clear screen command when launching the TUI.  When I scroll up I can see the previous commands and it would be better to be frozen at the title bar.

- [ ] **Need better feedback** This may be for both VS code and TUI but we need better feedback.  When I run SHOW SESSIONS I would expect the messages to show 0 rows returned if there are not open sessions.  But instead you're given nothing, now I'm wondering did it work or not.  We need better feedback on what's happening for all commands.  

- [ ] **When the window height it small** When the window height is small, default windows size the performance panel the up/down arrows work but its too small to show the frame.  I have to ctrl+m to get a view of what's happening.  Can we add a message that says viewing window too small use ctrl+m to maximize and view.  But not show that message when everything fits in the window.

## VS Code Extension on-going issues

- [ ] **Pipeline execution tree**  When running loops it should just keep restating the same node multiple times rather than print all the iterations.  That really gums up the view when it prints so much.  
- [x] **Pipeline visibility** — Resolved rendering failures in constrained VS Code viewports (75px-100px) by implementing 'Greedy' node extraction.
- [x] **Micro-UI Stabilization** — Optimized Results grid and Sidebar for high-density, small-footprint environments.

## Phase 9 Report-SQL — Post-Launch Items

See [Docs/Strategy/Report_SQL_Strategy.md](Docs/Strategy/Report_SQL_Strategy.md) for the full design, decisions, and phased delivery plan.

Active implementation tasks will be tracked here as each phase begins.

---
## Architecture Documentation Gaps  ** For Claude only**

The following architecture documents are missing. Identified 2026-04-14.

### High Priority
- [x] **LSP Architecture** — Written: `Docs/Architecture/LanguageServer.md`
- [x] **VS Code Extension Architecture** — Written: `Docs/Architecture/VSCodeExtension.md`
- [x] **Variable Scoping, Procedures & Dynamic Execution** — Written: `Docs/Architecture/VariableScoping.md`
- [x] **Expression Evaluation & Type System** — Written: `Docs/Architecture/ExpressionEvaluation.md`

### Medium Priority
- [x] **TUI Interactive Editor Architecture** — Written: `Docs/Architecture/TuiEditor.md`
- [x] **Parser / Lexer Deep Dive** — Written: `Docs/Architecture/ParserLexer.md`

### Lower Priority
- [ ] **Docker / Infrastructure Commands** — `DockerContainerManager` and `USE DOCKER` are referenced in the README but the spawn lifecycle, container polling, and session-teardown cleanup are undocumented.
- [ ] **Window Functions & Advanced Operators** — `ExternalWindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) supports signature-based grouping and disk-spilling for hyper-scale scenarios.

---

## 2026-04-15 Code Audit Findings (System Enrichment & Security)

### Security
- [x] **Path Resolution consistency** — Resolved across all connectors (Batch 1).
- [x] **Credential Leak Rule Coverage** — Expanded keyword list to 25+ sensitive tokens (Batch 4).
- [x] **SFTP Key handling** — Audit complete; no leaks found in logging (Batch 4).
- [x] **Add ENV to appsettings.json** — Exposed `Security:AllowedEnvVars` in `appsettings.json` and centralized configuration in `SecurityService`.

### Performance
- [x] **Window Function Spilling** — `ExternalWindowEngine` now supports signature grouping and multi-pass spilling to disk for incompatible signatures.
- [x] **Window Function Deep Spilling** — `ExternalWindowEngine` now handles block-level streaming for ranking functions to avoid materialization of large partitions.
- [ ] **Grouping Sets (ROLLUP/CUBE) Spilling** — `ExternalAggregateEngine` does not support `GroupingSet`. Multi-dimensional aggregates on large datasets will ignore the memory limit and OOM.
- [x] **CTE Materialization** — Refactored; however, true streaming for non-recursive CTEs is still a candidate for future optimization.
- [x] **AggregateEngine Memory Efficiency** — Refactored `SelectStatementHandler` to use `ExternalAggregateEngine` for hyper-scale scenarios.
- [x] **Set Operation Scaling** — `UNION ALL` now streams without buffering (Batch 4).

### Code Quality & Debt
- [x] **SRP Violation: MockSqlDataSource** — Refactored data seeding into `IMockDataSeeder` service.
- [x] **Sync-over-Async in Seeding** — Resolved; initialization is now task-based and awaitable (Batch 2).
- [x] **Handler Bloat** — `SelectStatementHandler` refactored; logic delegated to `SelectExecutionEngine` (Batch 4).
- [x] **Missing TruncateAsync** — Resolved for all relevant `IDataSource` implementations (Batch 1).
- [ ] **Expose hardcoded values** I saw this in the admin guide with a hardcoded value.  Can we expose this in the appsetting.json.  Orchestrator metrics are logged every 60 seconds (hardcoded).
- [ ] **Session values** Are there other session variables we need to expose to the user.  SHOW SESSIONS lists out all the active sessions with size.  Any values that should be configurable by admins.  We may need an admin way to clear a session.  I know the user can do a CLEAR SESSION in their script but if they forget the admin may need to come in a clear a big session.

### Testing Infrastructure
- [x] **Messy Data Regression tests** — Implemented and verified with `messy_data_load.etlsql` (Batch 3).
- [x] **Dialect Linter expansion** — `TOP PERCENT` and `ROWNUM` parsing/linting verified with cross-dialect validation tests (MSSQL vs Oracle vs Postgres).

---

## 2026-04-16 Documentation Audit Findings  ** For Claude only**

Full re-evaluation of all .md files against the current codebase. Items are ordered by user impact.

### Stale / Incorrect Content (fix before users hit broken examples)

- [x] **`Docs/Report_SQL_Guide.md` — Visual types table is wrong** — Fixed: removed Chart.js column, added all 18 visual types, updated renderer to ECharts.

- [x] **`Docs/Report_SQL_Guide.md` — Quick start uses old page structure syntax** — Fixed: STRUCTURE now uses CSS grid-template-areas (`'A'`); MAP slot quoting confirmed correct (`'A' = SalesChart`).

- [x] **`Docs/Report_SQL_Guide.md` — CREATE DATASET encryption syntax is wrong** — Fixed: documented three modes (MACHINE, PASSWORD, KEYFILE) with full examples.

- [x] **`Docs/Report_SQL_Guide.md` — SLICER MAPPINGS documentation is misleading** — Fixed: removed PARAMETER mapping role; documented correct ACTIONS-based pattern for SLICER and MULTISELECT.

- [x] **`Docs/Architecture/Reporting.md` — References ChartJsRenderer throughout** — Fixed: full rewrite to EChartsRenderer; all Chart.js references removed.

- [x] **`Docs/Architecture/Reporting.md` — AST node list is outdated** — Fixed: all 18 visual types listed; new fields (Styles, TypedSeries, EncryptionMode); new nodes (CreateContainerStatement, CreateNavigationStatement, SetReportMetadataStatement).

- [x] **`Docs/Architecture/Reporting.md` — Missing Phase 9B-9F features entirely** — Fixed: full rewrite adds EChartsRenderer, SvgChartRenderer, PdfExporter, DashboardServiceFactory, all new handlers, multi-report routes, and batch parameter endpoint.

### Missing Sections in Existing Docs

- [x] **`AGENTS.md` — No Report-SQL section** — Fixed: added Report_SQL_Guide.md to §2 table and added §2.5 with key syntax facts for `.rptsql` authoring.

- [x] **`Docs/Reference/Grammar.md` — No Report-SQL grammar** — Fixed: added Appendix A with formal grammar for all Report-SQL statements.

- [x] **`Docs/Report_SQL_Guide.md` — Missing statements and options** — Fixed: guide now covers SET REPORT TITLE/DESCRIPTION, STYLE, CREATE CONTAINER, CREATE NAVIGATION, FORMAT, COLORS, LEGEND, SUBTITLE, --format pdf, --manifest serve, and reports.json format.

- [x] **`Docs/Architecture/Reporting.md` — Missing multi-report hosting section** — Fixed: §9.2-9.4 document DashboardServiceFactory, reports.json, catalog endpoint, per-report API prefix, and runtime injection pattern.

### Missing Architecture Documents (carried from previous audit)

- [x] **LSP Architecture** — Written: `Docs/Architecture/LanguageServer.md`
- [x] **VS Code Extension Architecture** — Written: `Docs/Architecture/VSCodeExtension.md`
- [x] **Variable Scoping, Procedures & Dynamic Execution** — Written: `Docs/Architecture/VariableScoping.md`
- [x] **Expression Evaluation & Type System** — Written: `Docs/Architecture/ExpressionEvaluation.md`
- [x] **TUI Interactive Editor Architecture** — Written: `Docs/Architecture/TuiEditor.md`
- [x] **Parser / Lexer Deep Dive** — Written: `Docs/Architecture/ParserLexer.md`

### Missing ETL Language Features

These are capabilities common in production ETL tools that are either absent from the language or absent from the documentation (unclear which without deeper code investigation):

- [ ] **`PIVOT` / `UNPIVOT`** — No syntax or implementation for pivoting rows to columns or unpivoting columns to rows. Common in reporting prep and dimensional modeling. If implemented, it is undocumented in Grammar.md and Standard_Library.md.
This has been implemented.  You bring this up a lot why do you think this isn't there?

- [ ] **`CROSS APPLY` / `OUTER APPLY`** — Table-valued function application not documented. Used heavily in MSSQL for string splitting and JSON shredding scenarios.  

- [ ] **`EXCEPT` / `INTERSECT`** — Set difference and intersection operators. `UNION` and `UNION ALL` are documented; the other two set operators are not mentioned in Grammar.md.

- [ ] **Data quality `ASSERT` statement** — A first-class assertion statement (`ASSERT <condition> RAISE '<message>'`) would be valuable for data quality gates in ETL pipelines. Similar to dbt's `test` concept. Not currently implemented.

- [ ] **Schema drift detection** — No mechanism to detect when a source schema (column names, types) changes between runs. Common in production ETL as a guard against upstream changes breaking a pipeline silently.

### Missing Reporting Features

These are features common in reporting and BI tools that are absent from the Report-SQL language:

- [ ] **Conditional formatting on TABLE visuals** — Ability to highlight cells based on value (e.g., red if negative, green if above target). Standard in every BI tool. Would require a `FORMATTING (column = condition → color)` clause on TABLE visuals.

- [ ] **GAUGE visual type** — A radial gauge / speedometer for KPI dashboards (e.g., 73% of target). ECharts has native `gauge` support. Very common alongside CARD visuals for executive dashboards.

- [ ] **Funnel chart visual type** — Conversion funnel (e.g., impressions → clicks → purchases). ECharts `funnel` type is built-in. Common in marketing and sales reports.

- [ ] **Report parameter type declarations** — Currently parameters are untyped strings. A `PARAMETER @date AS DATE DEFAULT '2024-01-01'` declaration would let the engine validate input types and let DATEPICKER/SLIDER know their expected format.

- [ ] **Excel export (`--format xlsx`)** — The CLI supports `md`, `json`, and `pdf`. Excel is the most requested export format in enterprise reporting. `ExcelDataReader` is already a dependency; writing would require `ClosedXML` or `EPPlus`.

- [ ] **Cross-filtering between visuals** — Currently, clicking a chart element only fires explicit ACTIONS (DRILL_DOWN or SET_PARAMETER). A declarative `CROSS_FILTER = true` option that automatically filters all visuals on the same page by the clicked value would reduce boilerplate. Common in Power BI and Tableau.

- [ ] **Waterfall chart visual type** — Shows cumulative effect of sequential positive/negative values. Common in financial reporting (P&L, cash flow). ECharts supports this via bar chart with custom series.
