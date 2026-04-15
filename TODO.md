# ETL-SQL Development Roadmap
## TUI on-going issues
- [ ] **Prevent scroll up past window?**  I'm wondering if we start with a clear screen command when launching the TUI.  When I scroll up I can see the previous commands and it would be better to be frozen at the title bar.

- [ ] **When the window height it small** When the window height is small, default windows size the performance panel the up/down arrows work but its too small to show the frame.  I have to ctrl+m to get a view of what's happening.  Can we add a message that says viewing window too small use ctrl+m to maximize and view.  But not show that message when everything fits in the window.

## VS Code Extension on-going issues

## Phase 9 Report-SQL — Post-Launch Items

See [Docs/Strategy/Report_SQL_Strategy.md](Docs/Strategy/Report_SQL_Strategy.md) for the full design, decisions, and phased delivery plan.

Active implementation tasks will be tracked here as each phase begins.

---
## Architecture Documentation Gaps  ** For Claude only**

The following architecture documents are missing. Identified 2026-04-14.

### High Priority
- [ ] **LSP Architecture** — `ETL-SQL.LanguageServer` is a full LSP implementation (completions, diagnostics, hover, definition navigation, schema-aware autocomplete) with no architecture doc. Developers extending the engine and the VS Code/JetBrains integrations need this.
- [ ] **VS Code Extension Architecture** — `etl-sql-vscode` (TypeScript) covers syntax highlighting, inline lint diagnostics, and the `.rptsql` preview panel. Should document the extension/LSP handshake and how the preview panel connects to `ReportPlayer`.
- [ ] **Variable Scoping, Procedures & Dynamic Execution** — `VariableScopeManager`, `ProcedureExecutor`, `DECLARE`/`EXECUTE` semantics, output parameter binding, and how scope is inherited vs isolated across `RUN SCRIPT` nesting are undocumented.
- [ ] **Expression Evaluation & Type System** — `ExpressionEvaluator` is large and complex. Operator precedence, `CAST`/coercion rules, `CASE` handling, `NULL` propagation, and batch-row evaluation semantics need an architecture reference.

### Medium Priority
- [ ] **TUI Interactive Editor Architecture** — `Presentation.md` covers the output/data boundary but not the TUI itself: tab lifecycle, editor buffer, `EtlSqlHighlighter`, autocomplete integration, undo/redo stack, keyboard navigation.
- [ ] **Parser / Lexer Deep Dive** — `Engine.md` mentions the parser superficially. A developer adding a new statement type needs to understand tokenization strategy, the recursive-descent structure, ambiguous grammar resolution, and CTE/subquery handling.

### Lower Priority
- [ ] **Docker / Infrastructure Commands** — `DockerContainerManager` and `USE DOCKER` are referenced in the README but the spawn lifecycle, container polling, and session-teardown cleanup are undocumented.
- [ ] **Window Functions & Advanced Operators** — `ExternalWindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) supports signature-based grouping and disk-spilling for hyper-scale scenarios.

---

## 2026-04-15 Code Audit Findings (System Enrichment & Security)

### Security
- [x] **Path Resolution consistency** — Resolved across all connectors (Batch 1).
- [x] **Credential Leak Rule Coverage** — Expanded keyword list to 25+ sensitive tokens (Batch 4).
- [x] **SFTP Key handling** — Audit complete; no leaks found in logging (Batch 4).
- [ ] **Add ENV to appsettings.json** I see the note in the Admin guide.  I think we need to expose ENV() allowed in the appsettings file. Security:AllowedEnvVars.  Are any others missing?

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
