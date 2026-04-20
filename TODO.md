# ETL-SQL Development Roadmap

## Up Next
- [x] **Implement Buffer Manager**  Manages the resources, likely lives in the Orchestrator. (Completed: Implemented in Orchestrator.Execution)  

- [x] **Buffer Manager Reference Counting** We'll need a way to stop the Zombie Reference Problem. [2026-04-20] (Implemented via WeakReference + Zombie Sweep)

- [x] **Buffer Manager Lock bottleneck strategy**  (Completed: Implemented lock-free fast-path and throttled monitoring)

- [x] **Buffer Manager Thrashing** Implement Hysteresis to prevent the I/O death spiral. (Completed: Implemented exhaustion-state-aware queue processing)

- [x] **Encryption Centralization** Currently each script manages its own encryption on spills.  We should centralize this in the Orchestrator. [2026-04-20]

- [ ]  **Orchestrator retry logic**  The orchestrator should have retry logic for failed scripts.  The retry logic should be configurable.  The retry logic should be able to handle the case where the script fails part way through.  Thinking it uses the session to try and complete the script from where it left off.  

- [ ] **Document DAGs**  Need to show the user how to do a Directed Acyclic Graph style script where you have a main script that runs sub scripts.  It can use the PARALLEL keyword to run sub scripts in parallel.  Same if script B has a dependency on script A have an IF statement in the main script to say don't run be until A's file exists.

- [ ] **Chart Table Summaries** Need to add a couple of options to Table.  SummarizeRow, SummarizeColumn, the default will summarize any numeric columns.  We should also add a GrandTotal option, GrandTotalRow, GrandTotalColumn.  Finally we could add SummarizeColumn (Column1, Column2, ...) if the user wants to get down to just summarizing specific columns and not all numeric columns.

- [ ] **Chart Table Grid style** Need to add a GRID option ALL, NONE, HEADER, FOOTER, BOTH, LEFT, RIGHT, TOP, BOTTOM.  All is the default.  NONE will remove all grid lines.  HEADER will add a grid line around the header row, below the header row.  FOOTER will add a grid line around the footer (totals) row.  BOTH will add a grid line around the header and footer rows.  LEFT will add a grid line to the left of the first column.  RIGHT will add a grid line to the right of the last column.  TOP will add a grid line above the first row, below the header row.  BOTTOM will add a grid line below the last row, above the footer row.  Any combinations I'm forgetting?

- [ ] **Chart data labels** Need options to show data labels DATA_LABELS = ON|OFF WITH(DATA_LABELS_POSITION = TOP|BOTTOM|LEFT|RIGHT|CENTER, COLOR = 'COLOR', FONT_SIZE = 10, FONT_FAMILY = 'Arial', FONT_WEIGHT = 'NORMAL', STYLE = <style name>, FORMAT = <format string>)

- [ ] **Bar/line charts** SHOW_NO_DATA_PLACEHOLDER = ON|OFF this will but in a 0 value for the Y axis when there is no data for a given X axis value.  The default is OFF.  Prevents hidden chart gaps.

- [ ] **Gauge Types** GAUGE_TYPE= whatever styles ECharts has to offer

- [ ] **Conditional formatting will need compound statements** Conditional formatting will need AND and OR.  I suspect the rule writting will work just like a CASE statement 
```sql
FORMATTING ( 
  Revenue > 100000 AND Revenue < 200000 THEN 'Green'
  Revenue > 200000 AND Revenue < 300000 THEN 'Blue'
  Revenue > 300000 AND Revenue < 400000 THEN 'Red'
)
```

- [ ] **CREATE CONTAINER should have STRUCTURE** CREATE CONTAINER should work just like page where is has a structure that you can put visualizations inside of it and arrange them.  This gives you the ability to subdivide out the page.  This is missing from the document Report_SQL_Guide.md and so I'm not sure if it has this or not.  Right now the document says it will arrange them vertically but it should do the STRUCTURE 'A A / B C', MAP( 'A' = Revenue, 'B' = KPI, 'C" = Alert)
```sql
CREATE CONTAINER kpi AS BOX(
  STYLE (HEIGHT = 200)
  ,STRUCTURE = 'A / B'
  ,MAP (
    'A' = kpi
    ,'B' = myGraph
  )
);
```
This does not need the VISUALS keyword.  That can be removed it doesn't really make sense and would break consistency.

- [ ] **CREATE NAVIGATION** CREATE navigation in the Report_SQL_Guide.md is missing the options for ORIENTATION (HORIZONTAL, VERTICAL).

- [ ] **STYLE HEIGHT for all**  The STYLE of HEIGHT says it can only be used by CONTAINERs, that's not true it can be used by all objects.  You should be able to set a height and width for any object on the report page.

- [ ] **CREATE PAGE PARAMETERS**  CREATE PAGE PARAMETERS don't make any sense.  They should be removed.  If a user wants a parameter they just DECLARE @id int.  This just creates another way that is not intuitive and is clunky to use.  Remove it.

- [ ] **Allow images in buttons, text, and box slicers** All images to be inside of these objects to give it a real feel to it.  I'm thinking we add a variable type of IMAGE.  DECLARE @MyImage IMAGE = 'C:\\Users\\chuck\\Pictures\\image.png';   Thinking we restrict the types to jpg, png, and gif.  
---

## Large Dataset Handling — Gaps vs. `Docs/Strategy/LargeDatasets.md`

The spill infrastructure (`SpillStore`, `ExternalSortEngine`, `ExternalJoinEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine`) is complete and wired for query operations. The items below are the remaining unimplemented strategies from the design doc. Without them the acceptance criterion — `SELECT * INTO #SalesData FROM FactSales` (50M rows) on an 8 GB VM without OOM — cannot be met.

  #### [x] Strategy 2.3: Spill-to-Disk for `#temp` Tables
  Completed: Transparent row-level spilling with multi-chunk management and secure cleanup. Verified via `TempTableSpillTests`.

    - [x] Session Metadata: Persist variables, lineage, and temp table schemas to an SQLite metadata store. (Completed: Implemented in SessionStateManager using SqliteSessionMetadataStore)
    - [x] Spilled Data Persistence: Handle "detaching" spilled #temp table chunks from SpillStore transient cleanup to allow recovery after restart. (Completed: Enabled via IsPersistentSession flag and rehydration logic)
    - [x] Key Management: Ensure machine keys are used for encrypting saved session state. (Completed: Implemented machine-locked DPAPI/AES-256 encryption for session metadata)

  #### [x] Strategy 2.1: Streaming Batch Propagation
  Completed: Capped `SELECT` result buffer at `MaxLastResultRows` and stopped batch consumption early for non-redirected queries to prevent memory exhaustion. Verified via `StreamingCappingTests`.

  #### [x] Strategy 2.2: Chunked FOR Loop Pushdown
  Completed: Enabled native `OFFSET`/`FETCH` pushdown for paged execution loops in `ForeachStatementHandler` and `SelectStatementHandler`. Verified via `OpForeachSafetyTests`.

  #### 🟢 Low Priority — Strategy 2.4: Arrow Columnar Format

  - [ ] **`DataTable` (row-oriented, boxed `object[]`) is the core temp-table representation** — Replacing it with Apache Arrow columnar format would yield 10–50× speedup on aggregation-heavy workloads and dramatically lower memory for numeric columns. 
    - **Benefits Identified:**
        - **10–50x performance improvement** via SIMD/vectorized processing of columns.
        - **Memory density:** Avoids overhead of boxed objects; stores primitives in contiguous memory arrays.
        - **Zero-copy interoperability:** Enables high-speed handoff to Python/R/C++ analytical libraries.
        - **Native Spilling:** Arrow IPC format is a standard-compliant alternative for Strategy 2.3 spilling.
    - **Implementation Impact:**
        - **"Transplant vs. Feature":** Requires refactoring nearly every logic handler (Aggregate, Join, Sort) to use vectorized kernels instead of LINQ-over-Rows.
        - **Prerequisite:** Streaming (2.1) and Spilling (2.3) should be completed first to solve stability/OOM issues before moving to performance tuning with Arrow.
        - **Scope:** Treat as a standalone architectural migration project.
        - **Hybrid Approach:**  CREATE COLUMNAR TABLE #TempTable(...)   This allows both worlds to work without having to completely rewrite the engine.  We would just create another path.

- [ ] **Update Test names to be meaningful**  Names like Batch11RefinementTeest, Batch4EfficencyTests don't have any real meaning lets group the tests into logical groups and give them meaningful names.  

- [ ] **After TODO.md is complete** Change version from 0.5.0 to 0.6.0 in all documents and code.  Lets package the application into an installer for Windows, Linux, and Mac.  Also a VSIX for VS Code.  I want to be able to put these out on GitHub.  The application should be portable and reduced to as few files as possible.  What's else am I missing?  Do we create a client install and a server install?  Client is just whats needed to run VS code, ETL-SQL.LanguageServer.exe, etl-sql-report.exe, and etl-sql.exe, and etl-sql.TUI.exe.  Server has everything?  Or just give everyone everything all the time?  