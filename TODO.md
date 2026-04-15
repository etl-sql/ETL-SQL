# ETL-SQL Development Roadmap
## TUI on-going issues

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
- [x] **Window Functions & Advanced Operators** — `ExternalWindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) supports signature-based grouping and disk-spilling for hyper-scale scenarios.

---

## 2026-04-15 Code Audit Findings (System Enrichment & Security)

### Security
- [x] **Path Resolution consistency** — Resolved across all connectors (Batch 1).
- [x] **Credential Leak Rule Coverage** — Expanded keyword list to 25+ sensitive tokens (Batch 4).
- [x] **SFTP Key handling** — Audit complete; no leaks found in logging (Batch 4).

### Performance
- [x] **Window Function Spilling** — `ExternalWindowEngine` now supports signature grouping and multi-pass spilling to disk for incompatible signatures.
- [ ] **Window Function Deep Spilling** — While `ExternalWindowEngine` handles partition-level spilling, large SINGLE partitions (exceeding memory) still require block-level streaming for functions like `ROW_NUMBER()` to truly avoid all materialization.
- [ ] **Grouping Sets (ROLLUP/CUBE) Spilling** — `ExternalAggregateEngine` does not support `GroupingSet`. Multi-dimensional aggregates on large datasets will ignore the memory limit and OOM.
- [x] **CTE Materialization** — Refactored; however, true streaming for non-recursive CTEs is still a candidate for future optimization.
- [x] **AggregateEngine Memory Efficiency** — Refactored `SelectStatementHandler` to use `ExternalAggregateEngine` for hyper-scale scenarios.
- [x] **Set Operation Scaling** — `UNION ALL` now streams without buffering (Batch 4).

### Code Quality & Debt
- [x] **SRP Violation: MockSqlDataSource** — Refactored data seeding into `IMockDataSeeder` service.
- [x] **Sync-over-Async in Seeding** — Resolved; initialization is now task-based and awaitable (Batch 2).
- [x] **Handler Bloat** — `SelectStatementHandler` refactored; logic delegated to `SelectExecutionEngine` (Batch 4).
- [x] **Missing TruncateAsync** — Resolved for all relevant `IDataSource` implementations (Batch 1).

### Testing Infrastructure
- [x] **Messy Data Regression tests** — Implemented and verified with `messy_data_load.etlsql` (Batch 3).
- [ ] **Dialect Linter expansion** — `TOP PERCENT` and `ROWNUM` parsing/linting needs cross-dialect validation tests (MSSQL vs Oracle vs Postgres).
