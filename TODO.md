# ETL-SQL Development Roadmap

## Up Next

- [ ] **Strategy 2.4: Arrow Columnar Format**
    - `DataTable` (row-oriented, boxed `object[]`) is the core temp-table representation — We'll do a hybrid approach.
    - `CREATE COLUMNAR TABLE #TempTable(...)` will create a columnar table.
    - **Benefits Identified:**
        - **10–50x performance improvement** via SIMD/vectorized processing of columns.
        - **Memory density:** Avoids overhead of boxed objects; stores primitives in contiguous memory arrays.
        - **Zero-copy interoperability:** Enables high-speed handoff to Python/R/C++ analytical libraries.
        - **Native Spilling:** Arrow IPC format is a standard-compliant alternative for Strategy 2.3 spilling.
    - **Implementation Impact:**
        - Requires refactoring nearly every logic handler (Aggregate, Join, Sort) to use vectorized kernels instead of LINQ-over-Rows.
        - Prerequisite: Streaming (2.1) and Spilling (2.3) should be completed first.
        - **Hybrid Approach:** `CREATE COLUMNAR TABLE #TempTable(...)` allows both worlds to work without having to completely rewrite the engine.

- [ ] **Release Preparation (v0.6.0)**
    - Change version from 0.5.0 to 0.6.0 in all documents and code.
    - Package the application into an installer for Windows, Linux, and Mac.
    - Create a VSIX for VS Code.
    - Application should be portable and reduced to as few files as possible.
    - Determine client vs. server install structure (Client = VS Code UI + LanguageServer + CLI; Server = full engine).

---

## Technical Debt & Polish
- [ ] **Self-Updating/Portable CLI**: Ensure CLI is truly portable and standalone.
- [ ] **Health-Check Command**: Implement `etl-sql doctor` for verification of dependencies.
- [ ] **Standard Templates**: Provide a "Kitchen Sink" report template showcasing all Visual types.
- [ ] **Security Manifest**: Implement trusted-directory policies or script signing for secure deployments.