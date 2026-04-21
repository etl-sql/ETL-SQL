# ETL-SQL Development Roadmap

## Up Next

- [ ] **Strategy 0.7.0: Arrow Columnar Format**
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
---

## Technical Debt & Polish
- [ ] **Self-Updating/Portable CLI**: Ensure CLI is truly portable and standalone.
- [ ] **Health-Check Command**: Implement `etl-sql doctor` for verification of dependencies.
- [ ] **Standard Templates**: Provide a "Kitchen Sink" report template showcasing all Visual types.
- [ ] **Security Manifest**: Implement trusted-directory policies or script signing for secure deployments.

## GitHub Detected vulnerabilities
- [x] **GitHub Detected vulnerabilities — `npm audit` is now CLEAN (0 vulnerabilities)**
    - [x] **Prototype Pollution in sheetJS #2** — RESOLVED
        - `media/xlsx.full.min.js` was a dead artifact; the results panel now uses the React/Vite UI.
        - File has been removed. No live code reference to SheetJS/xlsx remains.
    - [x] **SheetJS Regular Expression Denial of Service (ReDoS) #4** — RESOLVED (same as above)
    - [x] **minimatch ReDoS vulnerability #1** — RESOLVED (minimatch upgraded transitively)
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/1
    - [x] **minimatch has a ReDoS via repeated wildcards #7** — RESOLVED
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/7
    - [x] **minimatch matchOne() combinatorial backtracking #9** — RESOLVED
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/9
    - [x] **minimatch nested *() extglobs ReDoS #8** — RESOLVED
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/8
    - [x] **js-yaml prototype pollution in merge #5** — RESOLVED
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/5
    - [x] **Regular Expression Denial of Service in debug #3** — RESOLVED
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/3
    - [x] **jsdiff Denial of Service in parsePatch/applyPatch #6** — RESOLVED
        - mocha upgraded to ^11.3.0 (removing `diff` vulnerability).
        - `serialize-javascript` pinned to `>=7.0.5` via `overrides` in package.json.
        https://github.com/AmericanSuperstar/ETL-SQL/security/dependabot/6