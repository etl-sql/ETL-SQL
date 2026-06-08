# ETL-SQL Development TODO List

Use this list to track and prioritize outstanding roadmap items, architecture modernization tasks, and documentation improvements.

---

## Roadmap for v0.11.0

### 1. Engine Performance & Scalability (Volcano Pipeline)

*   [ ] **End-to-End Query Streaming for Complex SELECT Paths**
    *   **Description**: Implement Phase 8 of [Query_Execution_Efficiency_Strategy.md](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/Strategy/Query_Execution_Efficiency_Strategy.md). The query execution engine currently falls back to full list materialization (via `.ToListAsync()`) on complex queries with joins, window functions, and group aggregations inside [SelectExecutionEngine.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/SelectExecutionEngine.cs).
    *   **Impact**: Eliminates Large Object Heap (LOH) fragmentation and memory exhaustion during large-scale sequential query execution (e.g., 20k+ rows).
    *   **Key Tasks**:
        *   Replace list buffering in `SelectExecutionEngine.cs` with chained `IAsyncEnumerable<Row>` streams.
        *   Define and enforce a unified capped result-retention contract for hosts (CLI, TUI, ReportPortal).

*   [ ] **Adaptive Memory Grants & Byte-Based Spilling**
    *   **Description**: Transition from hardcoded row-count spill thresholds to dynamic, byte-based memory grants.
    *   **Key Tasks**:
        *   Sample first row widths to estimate batch payload sizes.
        *   Dynamically trigger disk spilling in external engines (Sort, Join, Aggregate) using memory-grant allocations.

### 2. Lineage & Governance Enhancements

*   [ ] **Standard Tag Catalog Type Validation**
    *   **Description**: Enforce the tag schema metadata defined in [Lineage.md](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Lineage.md#L120-L177).
    *   **Key Tasks**:
        *   Add syntax linting to check that `@freshness` follows duration formatting (e.g., `1h`, `24h`).
        *   Enforce enum constraints for tags like `@classification` (`public`, `internal`, `confidential`, `restricted`) and `@quality` (`gold`, `silver`, `bronze`).

*   [ ] **Lineage Cycle Detection Warnings**
    *   **Description**: Graph traversal in [LineageTracker.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/LineageTracker.cs) and [LineageGraphRenderer.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Analysis/Lineage/LineageGraphRenderer.cs) is protected against infinite loops via `visited` sets. However, when a cycle is encountered, it is bypassed silently. We need a compiler/execution warning to alert the operator.

### 3. Data Lake & Analytical Capabilities

*   [ ] **Tier B Data Lake Support (Open Table Formats)**
    *   **Description**: Add native metadata parsing and directory traversal for open lakehouse formats (Apache Iceberg, Delta Lake, or Apache Hudi) on raw object storage (S3, ADLS, or GCS).
    *   **Impact**: Allows querying lakehouse formats directly without requiring a separate SQL query engine (like Snowflake or Databricks).

*   [ ] **Local Embedded SQL Engine (DuckDB)**
    *   **Description**: Integrate DuckDB as an in-process query execution engine to accelerate local analytical queries over Parquet/CSV files directly (without staging through engine memory).
