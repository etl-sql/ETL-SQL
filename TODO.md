# ETL-SQL Development Roadmap
## TUI on-going issues

## VS Code Extension on-going issues

## Phase 9 Report-SQL — Post-Launch Items

Phases 9A–9D are complete. The following items were deferred as out-of-scope for the initial launch or identified in the Phase 9 risk register as follow-up work.

### Dashboard Behavior
- [ ] **Rpt-4 HOLD** `STRUCTURE` string validation. `CreatePageStatementHandler` and the linter should validate the CSS grid template areas string: every letter in the `MAP(...)` must appear in `STRUCTURE`, and every letter in `STRUCTURE` must appear in the map. Mismatches produce a broken layout silently today.  Note: I would like to get the structure the way I want it first.  Hold on this item for now.

### Documentation
- [ ] **Page structure**  I don't think the STRUCTURE option is working. I'm using the example below and everything just went top to bottom in a single column.  I would like to see a 2x3 grid.  Maybe there needs to be better definition of the structure option.  
```sql
CREATE PAGE <name> AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = TotalRevenue,
    'B' = RevenueByRegion,
    'C' = RevenueByProduct,
    'D' = UnitsByMonth,
    'E' = SalesTable
  )
);
```
My initial draft was that the STRUCTURE option was listed like this 'A A / B C / D E' to represent a 2x3 grid.  I'm not sure if that's the best way to represent it, but it's what I came up with.  Maybe that's hard to implement but it gives you a better indication of what is happening.  I guess the assumption is it works top 

- [ ] **Need to add a way to create a new page/tabs**  Currently its rendered as a single page.  Need to be able to generate multiple pages and then we'll need a new structure that acts as the naviation tabs.  
```sql
CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = TotalRevenue,
    'B' = RevenueByRegion,
    'C' = RevenueByProduct,
    'D' = UnitsByMonth,
    'E' = SalesTable
  )
);
CREATE PAGE Detail AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = DetailTable
  )
);
CREATE NAVIGATION Tabs AS (
  PAGE = Main,
  PAGE = Detail
) WITH(TYPE = 'tabs', ORIENTATION = 'horizontal');
```
The navigation could be a tab, sidebar, or other layout.  We should be able to define the type of navigation and the layout of the navigation.

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
## Engine Tweaks
- [x] **Missing configuration**  The `Scheduler:MetricsIntervalSeconds` and `SleepIntervalSeconds` are now sourced from `appsettings.json`.
- [x] **Multiple appsettings**  Consolidated all host configuration into a single master `appsettings.json` in the `src/` root.