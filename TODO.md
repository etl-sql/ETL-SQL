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
- [ ] **Window Functions & Advanced Operators** — `WindowEngine` (PARTITION BY, ROW_NUMBER, RANK, etc.) is a footnote in `Engine.md`. Worth a dedicated section given the complexity of streaming window evaluation.

---
