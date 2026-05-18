# Changelog

All notable changes to ETL-SQL are documented here. This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions. Version numbers follow [Semantic Versioning](https://semver.org/).

---

## [0.7.0] — 2026-05-12

### Added

**Reporting & Interactive Dashboards**
- **Advanced Drill-Down**: Implemented `DRILL_IN` and `DRILL_DOWN` for hierarchical, in-place data exploration; added `DRILL_TO` for cross-report navigation with parameter state passing.
- **Paginated Reports**: Support for `PAGINATED = ON` reports featuring automatic header/footer repetition, multi-page data grid spans, and specialized snapshot formats.
- **ETL Notebooks (`.etlnb`)**: Native VS Code notebook support with cell-based execution, stateful REPL persistence, and cross-cell IntelliSense for connections and variables.
- **Cross-Visual Highlighting**: Power BI-style interactive filtering where clicking a chart segment highlights related data across all other visuals.
- **Ghost Rendering**: Enhanced interaction logic with "ghosting" (dimming) support for Line, Scatter, Pie, and Donut charts during highlighting.
- **New Visual Types**:
    - **MAP**: Integrated ECharts-based mapping with custom GeoJSON support (`MAP_FILE`).
    - **Specialized Charts**: Added `GAUGE`, `BOXPLOT`, `WATERFALL`, `BUBBLE`, `RADAR`, and `CANDLESTICK`.
    - **Input Visuals**: Added `TEXTBOX`, `NUMBERBOX`, and `CHECKBOX` for direct scalar parameter input.
    - **Interactive Slicers**: Support for `SLIDER` and `SEARCH` visual types with immediate dashboard re-rendering.
    - **Interactive Multi-Select**: New `MULTISELECT` visual type rendering as a checkbox list with automatic parameter synchronization.
- **Collapsible Containers**: Support for `COLLAPSABLE = ON`, `ICON`, and pinning logic for overlay drawers and sidebar panels.
- **Deferred Execution**: Added `RUN` button support with staged parameter batching (prevents report refresh on every slicer change).
- **Visibility Engine**: Standardized `VISIBLE = ON|OFF` syntax (replacing legacy `HIDDEN`); added support for dynamic visibility via `@variables`.
- **Enhanced Date Picking**: Native `RELDATEPICKER` (hybrid text + calendar) support.
- **Markdown Tables**: Full support for GFM-style tables in `TEXT` visuals via `marked.js` integration.

**Data, Lineage & Orchestration**
- **Shared Datasets**: Implemented a global dataset registry allowing reports to consume cached, shared data with automated background refreshes and access control.
- **OpenLineage Integration**: Support for exporting data lineage in OpenLineage-compliant JSON format.
- **Lineage 2.0 Engine**: 
    - **Standard Tag Library**: Defined 20 core lineage tags (`@pii`, `@sensitive`, etc.) with `@pii: true-wins` inheritance logic.
    - **Transformation Tracking**: Automated recording of transformation types (`Cast`, `Aggregation`, etc.) across the pipeline.
    - **Visualization**: Enhanced Mermaid-based lineage graphs with distinct shapes for Reports and Datasets.
- **Data Lake Connectors**: Native support for **Snowflake** and **BigQuery**.
- **Batch Separator**: Added `GO` keyword support for separating execution batches.
- **Improved Loops**: `FOR` loops now support implicit start values with `FOR @i TO 10`.
- **QUALIFY Clause**: Added T-SQL/Snowflake-style `QUALIFY` clause for filtering results based on window function values.
- **Window FILTER**: Support for the `FILTER (WHERE ...)` clause inside aggregate window functions.
- **@@FETCH_STATUS**: Added support for checking cursor/foreach fetch status.

**Security & Governance**
- **JWT Secret Generation**: New `GENERATE JWT_SECRET` command for securing report portal communications.
- **Proactive Guardrails**: Linter now warns on high-risk operations and blocks sensitive directory access more aggressively.
- **Decompression**: Added `DECOMPRESS FILE` and `DECOMPRESS DIRECTORY` statements to the specialized operations library.
- **PGP Engine Hardening**: Improved `PGP_KEY_PAIR` generation and validation logic.

**IDE, Tooling & UX**
- **Terminal IDE (TUI) 2.0**: Massive overhaul of the TUI with scrolling, smart copy, message panel optimization, and specialized visual rendering.
- **Unified IntelliSense**: 
    - New dot-aware suggestion engine with priority-based ranking and member-access discovery.
    - LSP support for `@`-prefix tag completions and documentation hovers.
    - Finalized purge of unstable semantic features for improved stability.
- **VS Code Preview**: Support for new chart types (Bubble, Radar, Candlestick, Map) and improved sidebar variable discovery.
- **Report SQL Audit**: Comprehensive rewrite of `Report_SQL_Guide.md` and inline help files to match current production state.
- **Deployment Packaging**: Integrated Windows MSI/ZIP, Linux `.deb`/ZIP, macOS DMG/ZIP, and platform-targeted VSIX generation into the release pipeline.

### Fixed
- **Multi-Select Regression**: Fixed a duplication bug where legacy dropdown logic was overwriting the new checkbox-list implementation.
- **Markdown Rendering**: Resolved issues where Markdown tables were displayed as raw text due to library interface mismatches.
- **IntelliSense Regressions**: Fixed missing connector option suggestions and asterisk expansion failures.
- **Portal State Bugs**: Resolved "white screen" and state synchronization issues in the report portal.
- **Slicer Logic**: Fixed null-reference errors in `renderSlicer` when actions were undefined.
- **Cross-Filesystem Paths**: Fixed portal publish flow failures when handling paths across different drives.
- **Gauge Rendering**: Resolved template string errors and implemented auto-formatting for decimal values.
- **Notebook Reliability**: Fixed "REPL process exited unexpectedly" and communication deadlocks by implementing atomic process lifecycle management and heartbeat checks.
- **Protocol Standardization**: Migrated REPL communication to strict PascalCase JSON with mandatory CRLF endings for Windows pipe stability.

### Changed
- **Sample Reorganization**: Expanded the curated `samples/` library and redirected generated sample outputs under `samples/output/` patterns for repository cleanliness.
- **Visibility Syntax**: Standardized report visibility on the unified `VISIBLE` property.
- **Directory Connections**: Statements like `COPY DIRECTORY` and `FILE_LIST` now natively accept `DIRECTORY` connection aliases as path arguments.
