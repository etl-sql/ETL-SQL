# ETL-SQL Development Roadmap

This document outlines the prioritized tasks for the next phase of ETL-SQL development.

## 1. VS CODE Extensions: Grammar Injection & Stability
- [x] **Native SQL IntelliSense**: Implement Grammar Injection to provide high-fidelity syntax highlighting for T-SQL/PL-SQL/Postgres blocks inside `EXECUTE ... BEGIN ... END`.
- [x] **Fragility Audit**: Stabilize the extension architecture to reduce breaking changes.
- [x] **Test Coverage**: Leverage the newly added `vitests` to ensure UI and language server components are robust.

## 2. Stabilization & Performance: O(1) Constraints
- [x] **Constraint Overhaul**: Finalize the transition of all `DataTable` constraint types (Primary Key, Unique, Foreign Key) to `HashSet`-based validation.
- [x] **Batch Performance**: Optimize `AddRow` and `Merge` operations to maintain O(1) performance as row counts increase.

## 3. Syntax Addition: Native Recursive CTEs
- [x] **Engine-level Recursion**: Implement native support for `WITH RECURSIVE` in the query evaluator.
- [x] **Heterogeneous Recursion**: Support traversing hierarchies that span multiple data sources (e.g., File -> Database -> In-memory).

## 4. Script parameters INPUT OUTPUT variables
- [x] **Script parameters**: We implemented INPUT and OUTPUT variables and added them to `ETL_SQL_Language_Reference.md`.

## 5. Architecture: Logging System Maturity
- [x] **DI Clean-up**: Complete the transition of all internal services (Handlers, Engines, Providers) to the `ILogger` Dependency Injection pattern.
- [x] **Diagnostic Removal**: Ensure all legacy `Console.WriteLine` and static `Logger` calls are removed.

## 6. Build System & Tooling
- [x] **Root Build Fix**: Restructure the solution or add `Directory.Build.props` to ensure `dotnet build` and `dotnet test` work reliably from the absolute root directory.
- [x] **Gitignore Polish**: Clean up the root directory and ensure temporary log files (e.g., `msbuild.log`) are ignored.

## 7. Language Modernization
- [x] **C# 12 Primary Constructors**: Perform a codebase-wide pass to adopt Primary Constructors, reducing boilerplate in AST nodes and DI-heavy services.

## 8. VS code darkmode causing the result table to be unreadable
- [x] **VS code darkmode causing the table to be unreadable**: Fixed via CSS variables and Tabulator theme integration in `resultsPanel.ts`.

## 9. VS code the run buttons are not visible when that panel is not in context.
- [x] **VS code the run buttons are not visible when that panel is not in context.**: This causes a two step process to run a script.  First you have to click on the panel to bring it into context and then you can click the run button.  Can we fix this?   

## 10. Need to add precision to variable data types in documentation
- [x] **Need to add precision to variable data types in ETL_SQL_Language_Reference.md**: 

## 11. Need a way to graphically view progress of ETL scripts
- [x] **Need a way to graphically view progress of ETL scripts**: (Design Document Created: [ExecuteTree.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/ExecuteTree.md))
Is this possible?
In the console window I can see building a tree structure of the script and then executing it.  Can we do the same thing in the VS Code extension?  
- As we execute each node in the tree we can color it green (check mark), yellow (in progress), or red (error) depending on the result.  
- Show a progress bar for each node that shows the percentage of completion.  
- The lines (branches) between the nodes should show the number of rows processed by that statement
- Each statement is a node on the tree
- Parallel statements are on the same level so they branch out from the parent node above them
- How do we get it to fit the screen?  The tree can get very large.  I feel like the right hand side of the screen is best since the code will live on the left.
- Wishlist: On vs code when you hover over a node it will show you the satement running and the results of that statement in a tooltip (Top 10 rows).  Also when you click on a node it will show you the results of that statement in the results panel.  The results panel should be the same as the results panel in the console window.
- Metrics for each node (CPU, Memory, Rows Processed, etc.) it will give a good indication of which one is a problem.
