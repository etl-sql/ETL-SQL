# ETL-SQL Development Roadmap

## Up Next

- [ ] **Code review round 2** — findings below, implement separately per priority.

  #### 🧪 Linting Gaps

  - [ ] **No linter rule: `MULTISELECT` / `SLICER` without `SOURCE`** — The parser allows it and silently produces a broken visual. Add `VisualSourceRequiredRule` that flags these as errors (not warnings).

  - [ ] **No linter rule: required `MAPPINGS` per visual type** — `BAR` without X+Y, `PIE` without LABEL+VALUE, `CARD` without VALUE are all accepted by the parser but produce empty/broken charts at runtime. Add `VisualMappingCompletenessRule` to catch these at lint time.

  - [ ] **No linter rule: deprecated connector syntax** — The parser throws immediately on `FILE(...)` (should be `FLATFILE`), but a linter rule would give a friendlier message during development with a suggestion to use the current syntax.

- [ ] **KILL JOB missing** This was referenced several times in different documentations but apparently it was never implemented.  After running SHOW JOBS the user should see the JOB ID and be able to KILL that job if they need to.  The KILL command should do a graceful end to the job, logging that it was killed by user, clean up SESSION, clean up incomplete files, rollback DB transactions if possible.  The KILL is meant to be somewhat immediate so we want to respect that idea as far as how far we go to gracefully cleanup.
---

## Large Dataset Handling — Gaps vs. `Docs/Strategy/LargeDatasets.md`

The spill infrastructure (`SpillStore`, `ExternalSortEngine`, `ExternalJoinEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine`) is complete and wired for query operations. The items below are the remaining unimplemented strategies from the design doc. Without them the acceptance criterion — `SELECT * INTO #SalesData FROM FactSales` (50M rows) on an 8 GB VM without OOM — cannot be met.

  #### 🔴 High Priority — Strategy 2.3: Spill-to-Disk for `#temp` Tables

  - [ ] **`#temp` table accumulation has no spill mechanism** — `InsertStatementHandler` writes every inserted row directly into the in-memory `DataTable` stored in `_tempTables`. There is no threshold check, no `SpillStore` hook, and no overflow path. A script doing `INSERT INTO #big ... ` (millions of rows) or `SELECT * INTO #big FROM large_source` will OOM the host. **Design in `LargeDatasets.md §5`:**
    - `TempTableInfo` gains a nullable `SpillStore` field (path to GZip-compressed NDJSON spill file — the existing `SpillStore` class already provides the writer/reader).
    - `InsertStatementHandler` checks row count against `TempTable:SpillThresholdRows` (config, default 1,000,000) after each batch; when exceeded, flushes in-memory rows to the spill file and clears the buffer.
    - All `#temp` table read paths (SELECT, JOIN probe side, FOREACH source, etc.) must transparently merge spill pages with the in-memory buffer when a spill file is present.
    - `DROP TABLE` and session disposal must delete the spill file.
    - `SHOW TABLES` should surface a `(spilled)` indicator when the table has overflow pages on disk.

  #### 🟡 Medium Priority — Strategy 2.1: Streaming Batch Propagation

  - [ ] **`SelectStatementHandler` merges all batches before returning** — The INTO path correctly calls `WriteBatches()` and streams, but the non-INTO path (plain `SELECT`) still collapses all batches into a single `DataTable` via `ReadBatches()` → merge loop before capping at `MaxLastResultRows`. For large sources this materializes the full dataset in RAM before the display cap is applied. **Fix:** apply the display cap during the merge loop (stop consuming batches once `MaxLastResultRows` is reached) and log a "results truncated to N rows" message. This avoids buffering rows beyond what will ever be shown.

  #### 🟢 Low Priority — Strategy 2.2: Chunked FOR Loop Pushdown

  - [ ] **`ForeachStatementHandler` paginates in-process, not at the source** — The handler re-issues the driving query with `OFFSET`/`LimitCount` per page, which causes the source connector to re-execute the full query N times (once per page). For SQL connectors that support native `OFFSET ... FETCH`, this should be pushed down as a single parameterized query variant per page rather than full re-execution. Low priority — only affects `FOREACH` over SQL sources larger than one page; flat-file and in-memory sources are unaffected.

  #### 🟢 Low Priority — Strategy 2.4: Arrow Columnar Format

  - [ ] **`DataTable` (row-oriented, boxed `object[]`) is the core temp-table representation** — Replacing it with Apache Arrow columnar format would yield 10–50× speedup on aggregation-heavy workloads and dramatically lower memory for numeric columns. Explicitly deferred — scope as a separate architectural migration project after strategies 2.1–2.3 are validated in production.

