# ETL-SQL Development TODO List

Use this list to track and prioritize outstanding roadmap items, architecture modernization tasks, and documentation improvements.

---

## v0.10.0 Release Blockers

These items were identified during the v0.10.0 release-readiness code and security review.

*   [x] **Critical: Require Authentication for Orchestrator Ad-Hoc Job Routes**
    *   **Description**: `POST /jobs` and `DELETE /jobs/{id}` in `src/ETL-SQL.Orchestrator.Service/JobApiEndpoints.cs` accept requests without an API-key check. The shipped Linux service and Docker configuration expose the Orchestrator on all interfaces while `Orchestrator:ApiKey` defaults to empty, allowing a remote caller to submit arbitrary ETL-SQL for execution.
    *   **Key Tasks**:
        *   Require authentication for every job submission, cancellation, status, scheduling, bundle, script, and management route except explicit health endpoints.
        *   Fail startup, or bind only to loopback, when no API key is configured.
        *   Generate and configure matching Orchestrator and Portal API keys in installers and deployment examples.
        *   Add integration tests proving missing or invalid credentials cannot submit or cancel jobs.

*   [x] **High: Prevent Path and ETL-SQL Injection in Spec-Generated Dataset Modules**
    *   **Description**: `src/ETL-SQL.App/App/PipelineGenerator.cs` uses dataset names directly in module filenames, `PRINT` text, and `RUN SCRIPT` statements. A crafted dataset name can escape the modules directory or inject generated ETL-SQL.
    *   **Key Tasks**:
        *   Restrict dataset names to a documented safe identifier format.
        *   Normalize each generated module path and verify it remains under the modules directory.
        *   Escape all generated ETL-SQL string literals.
        *   Add traversal, path-separator, quote, newline, reserved-name, and duplicate-normalized-name tests.

*   [x] **High: Revalidate REST Redirect Targets Against Egress Policy**
    *   **Description**: `src/ETL-SQL.Connectors/Rest/RestDataSource.cs` validates initial and generated request hosts, but its shared default `HttpClient` follows HTTP redirects automatically. An allowed endpoint can redirect to a blocked internal host and bypass `SecurityService.ValidateHost`.
    *   **Key Tasks**:
        *   Disable automatic redirects for REST and OAuth token requests.
        *   Follow redirects explicitly with a bounded redirect count and validate every target host.
        *   Define whether cross-host redirects may retain authorization or other sensitive headers.
        *   Add SSRF regression tests for blocked redirect targets and redirect loops.

*   [x] **Medium: Restore Snowflake `.p8` Private-Key Authentication**
    *   **Description**: `src/ETL-SQL.Connectors/Snowflake/SnowflakeDataSource.cs` now calls `ValidateFileType` for `PRIVATE_KEY_FILE`, but `.p8`, the documented Snowflake private-key extension, is absent from the allowed extension list in `src/ETL-SQL.Core/SecurityService.cs`.
    *   **Key Tasks**:
        *   Allow `.p8` specifically for Snowflake private-key input without weakening unrelated file connectors.
        *   Retain `ResolvePath` and protected-directory validation.
        *   Add tests proving documented `.p8` keys are accepted and traversal/system paths remain blocked.

*   [x] **Medium: Make the NuGet Pre-Release Audit Reliable on the Pinned SDK**
    *   **Description**: `scripts/Test-PreRelease.ps1` handles the .NET 10.0.300 `dotnet list package --outdated` null-reference failure, but the same SDK failure for `--deprecated` and `--vulnerable` aborts the release gate before build and tests.
    *   **Key Tasks**:
        *   Use an audit command or SDK version that reliably reports vulnerable and deprecated packages with central package management.
        *   Do not silently skip vulnerability results; fail with a clear actionable message if no authoritative audit can run.
        *   Add a script-level test or CI job proving the dependency-audit phase completes.

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

### 3. Data Lake & Analytical Capabilities, review value

*   [ ] **Tier B Data Lake Support (Open Table Formats)**
    *   **Description**: Add native metadata parsing and directory traversal for open lakehouse formats (Apache Iceberg, Delta Lake, or Apache Hudi) on raw object storage (S3, ADLS, or GCS).
    *   **Impact**: Allows querying lakehouse formats directly without requiring a separate SQL query engine (like Snowflake or Databricks).

*   [ ] **Local Embedded SQL Engine (DuckDB)**
    *   **Description**: Integrate DuckDB as an in-process query execution engine to accelerate local analytical queries over Parquet/CSV files directly (without staging through engine memory).
