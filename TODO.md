# ETL-SQL Development Roadmap
## TUI on-going issues

## VS Code Extension on-going issues
- [ ] **Execute Tree Clear** Each time I execute either all or selected the execute tree should be cleared an should start over.  Currently it just keeps adding to the tree view.
- [ ] **Variable Values** Variable values should not display on the sidebar, that was added recently.  I think the code is in place but they are not displayed.
- [ ] **Export to CSV** Export to csv should be added to the results grid context menu. It was but at some point it seems to have disappeared.
- [ ] **Settings cleanup** Setting is really messy, it should just need a pointer to where the exe files are and do you want to show debugging or not.  I don't know of any other options needed at this time. 
- [ ] **Add .rptsql extension** rptsql extension is not supported.  Its really the same as etlsql extension except a button should appear so that the user can preview the report in a new panel.  Should work like Markdown preview.  The report preview is already an option so there shouldn't be much to do here.

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
### Security

- [x] **CR-S1** — **Dashboard parameter values are injected as ETL-SQL source text (script injection).**
  Verified fix via `DashboardInjectionTests.cs`. Parameters are injected directly into scope.

### Quality

- [x] **CR-Q1** — **`JsonFunctions.cs` uses bare `catch {}` blocks that swallow fatal exceptions.**
  Multiple `catch { return null; }` and `catch { return 0m; }` blocks in JSON scalar functions catch all exceptions, including `OutOfMemoryException` and `StackOverflowException`.
  - Files: `src/ETL-SQL.Engine/Functions/JsonFunctions.cs` lines 69, 93, 116, 131, 150, 209, 248
  - Fix: Replace with `catch (Exception ex) when (ex is not OutOfMemoryException)` to allow fatal exceptions to propagate.

- [x] **CR-Q2** — **`ExplainStatementHandler` detects `DISTINCT` via string-matching regenerated SQL instead of the AST flag.**
  Line ~239 uses `select.ToSql().Contains("DISTINCT")` to decide whether to show a Distinct operator in the plan. If `ToSql()` serializes differently, the plan silently omits the step.
  - Files: `src/ETL-SQL.Engine/Handlers/ExplainStatementHandler.cs` ~line 239
  - Fix: Use `select.IsDistinct` (AST property) directly.

- [x] **CR-Q3** — **Engine.md does not distinguish streaming aggregate (always external) from buffered aggregate (external only at 100k rows).**
  The architecture doc implies both paths use the same threshold. The streaming aggregate path bypasses the threshold check entirely and always uses `ExternalAggregateEngine` regardless of row count, which is not documented.
  - Files: `Docs/Architecture/Engine.md`

---

### Test Design Concerns (TD)

- [ ] **TD-1** — **`PushdownTests` verifies "no exception thrown" rather than actual SQL or data.**
  `MockDatabaseSource.ExecuteRawSql` always returns an empty `DataTable`. Tests that call `EXECUTE MyDb BEGIN SELECT ... END` confirm the handler runs without error, but cannot verify the correct SQL was sent to the remote or that data was returned and mapped correctly. Pushdown correctness is invisible.
  - Files: `tests/ETL-SQL.Tests/Statements/PushdownTests.cs`, `tests/ETL-SQL.Tests/TestHelpers.cs`
  - Fix: Extend `MockDatabaseSource` to record executed SQL strings (it already has `ExecutedSql` list) and assert that the pushed-down SQL matches expected content; return a non-empty `DataTable` for queries that expect rows.

- [ ] **TD-2** — **`SecurityHardeningTests.TestPermissionOverride_AllowsLargeCount` only checks a negative.**
  The test asserts `securityError == false` (the 100-file limit did NOT fire) but does not verify that the operation actually attempted to process the files. A bug that silently skipped the operation entirely would also pass this test.
  - Files: `tests/ETL-SQL.Tests/Engine/SecurityHardeningTests.cs`
  - Fix: Also assert that `result.Diagnostics` contains the expected "file not found" errors (one per iteration), proving the engine attempted each delete operation.

- [ ] **TD-3** — **`CredentialLeakRuleTests.TestScoping` expects 2 warnings without explaining why the outer `@key = 'public-key'` also fires.**
  The variable name `@key` is likely what triggers the rule (name contains "key"), not the value "public-key". But the test comment only mentions the inner "private-secret" assignment. A future reader will not understand why the outer PRINT also fires. If the intent is to test re-declaration scoping, the outer variable should use a non-sensitive name.
  - Files: `tests/ETL-SQL.Tests/Engine/CredentialLeakRuleTests.cs`
  - Fix: Either rename the outer variable to `@publicData` to make the test clearly about only one sensitive name triggering, or add a comment explaining that `@key` (the name itself) always matches the sensitive-name pattern regardless of value.
  - Fix: Add a note clarifying that `streamAggregate = true` routes directly to `ExternalAggregateEngine` unconditionally, while the legacy buffered path uses it only after 100k rows are accumulated.

---