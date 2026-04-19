# ETL-SQL Development Roadmap

## Up Next
---

## Large Dataset Handling — Gaps vs. `Docs/Strategy/LargeDatasets.md`

The spill infrastructure (`SpillStore`, `ExternalSortEngine`, `ExternalJoinEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine`) is complete and wired for query operations. The items below are the remaining unimplemented strategies from the design doc. Without them the acceptance criterion — `SELECT * INTO #SalesData FROM FactSales` (50M rows) on an 8 GB VM without OOM — cannot be met.

  #### [x] Strategy 2.3: Spill-to-Disk for `#temp` Tables
  Completed: Transparent row-level spilling with multi-chunk management and secure cleanup. Verified via `TempTableSpillTests`.

    - [ ] Session Metadata: Persist variables, lineage, and temp table schemas to an SQLite or JSON file.
    - [ ] Spilled Data Persistence: Handle "detaching" spilled `#temp` table chunks from `SpillStore` transient cleanup to allow recovery after restart.
    - [ ] Key Management: Ensure machine keys are used for encrypting saved session state.

  #### 🟡 Medium Priority — Strategy 2.1: Streaming Batch Propagation

  - [ ] **`SelectStatementHandler` merges all batches before returning** — The INTO path correctly calls `WriteBatches()` and streams, but the non-INTO path (plain `SELECT`) still collapses all batches into a single `DataTable` via `ReadBatches()` → merge loop before capping at `MaxLastResultRows`. For large sources this materializes the full dataset in RAM before the display cap is applied. **Fix:** apply the display cap during the merge loop (stop consuming batches once `MaxLastResultRows` is reached) and log a "results truncated to N rows" message. This avoids buffering rows beyond what will ever be shown.

  #### 🟢 Low Priority — Strategy 2.2: Chunked FOR Loop Pushdown

  - [ ] **`ForeachStatementHandler` paginates in-process, not at the source** — The handler re-issues the driving query with `OFFSET`/`LimitCount` per page, which causes the source connector to re-execute the full query N times (once per page). For SQL connectors that support native `OFFSET ... FETCH`, this should be pushed down as a single parameterized query variant per page rather than full re-execution. Low priority — only affects `FOREACH` over SQL sources larger than one page; flat-file and in-memory sources are unaffected.

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

