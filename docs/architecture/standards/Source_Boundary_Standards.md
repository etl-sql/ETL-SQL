# Source Boundary Standards

This document establishes the official project layering, namespace boundaries, and component ownership constraints for the **ETL-SQL** codebase. All new code files and project references must comply with these architectural divisions.

---

## 1. Subsystem Directory & Layering

The codebase is split into distinct dependency layers to maintain clean separation of concerns and ensure testability. The target home of each subsystem is defined below:

```
┌────────────────────────────────────────────────────────┐
│                      Host Shells                       │
│    (Portal, VS Code, TUI, CLI, Orchestrator)     │
└───────────┬──────────────┬──────────────┬──────────────┘
            │              │              │
            ▼              ▼              ▼
┌──────────────────┐ ┌───────────┐ ┌─────────────────────┐
│  ReportHosting   │ │ Analysis  │ │      Reporting      │
└───────────┬──────┘ └─────┬─────┘ └──────────┬──────────┘
            │              │                  │
            ▼              ▼                  ▼
┌────────────────────────────────────────────────────────┐
│                         Engine                         │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│                          Core                          │
│                (AST, Parser, Contracts)                │
└────────────────────────────────────────────────────────┘
```

`ETL-SQL.Reporting.Contracts` is a BCL-only semantic-contract leaf alongside Core. Reporting depends
on it; Core does not. It contains renderer-neutral `ChartSpec`, typed chart data, and `PlotPlan`
contracts, never renderer, export, host, or pixel-emission code.

---

## 2. Component Ownership Guidance

### 2.1 `ETL-SQL.Core` (Core Language Contracts)
- **Role**: Central definitions shared by all runtimes, engines, and extensions.
- **Includes**: AST records, lexer/parser contracts, basic data types, and shared model definitions.
- **Rules**: Core must **never** depend on any executables, database providers, host shell applications, or external orchestration engines.

### 2.2 `ETL-SQL.Engine` (Engine Execution)
- **Role**: Evaluating and executing compiled scripts.
- **Includes**: Evaluator orchestration, statement handlers, query engines, `#temp` table execution, spill-to-disk controllers, transactional states, and Zero-Trust path resolution.
- **Rules**: Engine code provides execution contexts but must remain independent of specific host UI controllers or IDE behaviors.

### 2.3 `ETL-SQL.Connectors` (Data Connectors)
- **Role**: Data provider I/O adapters.
- **Includes**: Wire connections for MSSQL, Postgres, Oracle, Snowflake, SFTP, REST APIs, and FlatFiles. Owns exception wrapping, credential authentication mapping, and read/write converters.
- **Rules**: Connector projects must not depend on reporting layers or host applications.

### 2.4 `ETL-SQL.Analysis` (Static Language Analysis)
- **Role**: Analyzer intelligence.
- **Includes**: Syntax linters, cross-dialect safety checkers, lineage parsers, query explain plan builders, quick fixes, and diagnostic helpers.
- **Rules**: Analysis runs on AST and manifest structures. It must not execute scripts directly or communicate over network/connector I/O.

### 2.5 `ETL-SQL.Reporting` (Reporting Semantics)
- **Role**: Dashboard layouts and visual definitions.
- **Includes**: `ReportManifest` schema validators, visual page and container grids, styling attributes, actions, and chart representations.
- **Rules**: Reporting projects must remain independent of runtime environments (e.g. they should define what to render, not how to host the HTTP server).

### 2.5.1 `ETL-SQL.Reporting.Contracts` (Renderer-Neutral Reporting Contracts)
- **Role**: Stable semantic boundary shared by reporting resolvers and output backends.
- **Includes**: Versioned `ChartSpec`, typed columnar chart data, deterministic `PlotPlan`, and semantic
  conformance projections.
- **Rules**: Must remain BCL-only with no project or package references. It must not reference Core,
  ECharts, SVG/Skia, PDF, terminal presentation, exporters, or host shells.

### 2.6 `ETL-SQL.ReportHosting` (Report Execution Hosting)
- **Role**: Reusable dashboard session coordinator.
- **Includes**: Visual refresh session state, report manifest caching, background data refresh timers, and parameter binding engines.
- **Rules**: ReportHosting sits between the Engine execution layer and Report controllers.

### 2.7 Host Shells
- **Role**: Runtimes, TUI/CLI tools, web portals, and extensions (e.g., `Portal`, `ReportPlayer`, `etl-sql-vscode`, `TUI`, `App`).
- **Rules**: Host shells must remain thin. They must not implement parser syntax, custom query evaluation logic, or private reporting manifest behaviors. Instead, they must delegate all domain logic to the lower library boundaries.

---

## 3. Reference Diagram & Dependencies

1. **No Circular References**: Dependencies must only flow downwards.
2. **Horizontal Isolation**: Connectors cannot reference other connectors. Host shells cannot reference sibling host shells (e.g., `Portal` cannot depend on `ReportPlayer` or vice versa).
3. **Storage Access**: No host controller or service may write directly to the local filesystem using `System.IO.File`. All writes must route through the `IArtifactStorage` abstraction resolved from the engine core.

---

## References

- [Engine Coding Standards](Engine_Coding_Standards.md)
- [Connectors Standards](Connectors_Standards.md)
- [Source Boundary Migration Plan](../roadmaps/Source_Boundary_Migration_Plan.md)
