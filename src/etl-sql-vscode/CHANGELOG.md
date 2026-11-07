# Changelog

All notable changes to the "ETL-SQL Tools" extension for Visual Studio Code are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.22.0] — 2026-11-04

### Added
- **First Public Release** of the official ETL-SQL extension for Visual Studio Code.
- **Language Server & IntelliSense**:
  - Syntax highlighting for `.etlsql` and `.rptsql` files.
  - Live schema autocomplete connecting directly to active data connections.
  - Cross-dialect linting and real-time syntax error reporting.
  - In-editor hover cards providing function signatures, parameter descriptions, and runnable examples.
  - Session variable (`@var`) and temporary table (`#temp`) tracking across script sessions.
- **Interactive Query Execution**:
  - `F5` execution for entire scripts or highlighted selections.
  - Interactive multi-result grid featuring column sorting, real-time filtering, and paging.
  - Side-by-side **Compare Mode** for diffing two result sets.
  - Per-statement execution profiling with timing and memory metrics.
- **Report-SQL Live Preview (`.rptsql`)**:
  - Split-screen live preview that renders interactive dashboards alongside code.
  - Native SVG chart generation for Bar, Line, Area, Point, and Card visuals.
  - Interactive controls including slicers, cascading filters, date pickers, sliders, and cross-highlighting.
  - One-click export to native SVG, PDF, and Markdown from the preview panel.
- **Notebook Support (`.etlnb`)**:
  - Cell-based iterative execution model sharing a single live session.
  - Inline Mermaid lineage diagrams rendered directly in cell output.
  - One-click export from notebook to standalone `.etlsql` script.
- **In-Stream Data Quality & Lineage**:
  - Grammar-level `EXPECT` validation gates (`EXPECT NOT NULL`, `EXPECT UNIQUE`, `EXPECT >= 0`, regex matches).
  - Pluggable failure actions: `ON FAILURE THROW`, `ON FAILURE WARN`, and `ON FAILURE QUARANTINE`.
  - First-class contextual metadata tags (`/* @d: ...; @owner: ... */`).
  - Automated column-level lineage tracking (`eng.lineage`) with OpenLineage export for Marquez and DataHub.
- **Spec-to-Script AI Generation**:
  - Context menu command **ETL-SQL: Generate Script from Spec** for PDF, Word, Excel, CSV, JSON, and text specifications.
  - Generates pipeline scaffolding with schemas, quality gates, quarantine, and governance tags via Gemini or configured AI provider.
- **Built-in Security & Safety Guards**:
  - Project sandbox preventing script execution from reading or writing files outside the workspace.
  - Credentials guard warning immediately when plaintext passwords or connection strings are detected on save.
  - Save policy encryption automatically converting flagged plaintext credentials into `ENC:...` strings.
- **Multi-Platform Bundling**:
  - Self-contained native binaries included for Windows x64, Linux x64, and macOS (Intel & Apple Silicon).
