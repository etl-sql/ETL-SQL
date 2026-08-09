# User and Operator Guides

[« Back to parent](../README.md)

ETL-SQL narrative guides are organized by audience and stage: onboarding, core features, tool integrations, and operational patterns.

---

## 1. Onboarding

Getting started with the engine, writing your first pipeline, or migrating from a legacy layout.

| Page | Description |
| :--- | :--- |
| [ETL-SQL User Manual: Thinking in Pipelines](onboarding/getting-started.md) | Welcome to ETL-SQL. Narrative onboarding for transitioning from single-database SQL to multi-context data flows. |
| [Quickstart Guide](onboarding/QUICKSTART.md) | Standard quickstart runbook for terminal and local installations. |
| [ETL-SQL Migration Guide (v0.18.0)](onboarding/migration-guide.md) | Migration baseline and compatibility notes for upgrading deployment configurations to v0.18.0. |

---

## 2. Feature Guides

Deep dives into the capabilities, syntax extensions, and execution behaviors of the engine.

| Page | Description |
| :--- | :--- |
| [Validating Data Quality](feature-guides/data-quality.md) | Declare inline row-value rules using `@expect`/`@fail` and route failed rows with `ON FAILURE`. |
| [Orchestrating Pipelines & DAGs](feature-guides/pipelines-and-dags.md) | Coordinate multi-source tasks using `RUN SCRIPT`, `PARALLEL`, loops, try-catch, and scheduling. |
| [Report-SQL Scripting Guide](feature-guides/report-sql.md) | Author `.rptsql` dashboards, visuals, grids, actions, bindings, and portal publishing hooks. |
| [Data Stewardship and Impact Analysis](feature-guides/data-stewardship-impact.md) | Extract, query, and inspect lineage metadata and metadata tags before pushing changes. |
| [Report Ownership & Data Freshness Badges](feature-guides/report-badges-freshness.md) | Configure trust, ownership, and freshness badges in report headers and Portal catalog cards. |
| [Testing Guide](feature-guides/testing.md) | Strategy for engine contributors, testing mock connectors, and running verification suites. |

---

## 3. Tooling & Integrations

IDE integrations, VS Code extensions, web designers, and portal user guides.

| Page | Description |
| :--- | :--- |
| [VS Code Extension](tooling/vscode-extension.md) | Configure autocomplete, syntax validation, and hover help powered by the Language Server Protocol. |
| [ETL-SQL Notebooks (.etlnb)](tooling/notebook-guide.md) | Run stateful interactive markdown cells and scratch scripts directly inside VS Code. |
| [Visual Report Builder Guide](tooling/report-builder.md) | Use the drag-and-drop WYSIWYG editor and 12-column grid to assemble Report-SQL layouts. |
| [ETL-SQL Portal: User Guide](tooling/portal-user.md) | Consume, run, filter, favorite, subscribe to, and share published reports. |
| [Catalog Search & Discovery Guide](tooling/catalog-search.md) | Search the Portal metadata catalog for tables, reports, tags, and lineage paths. |

---

## 4. Patterns & Best Practices

Standard recipes, operational playbooks, and troubleshooting diagnostics.

| Page | Description |
| :--- | :--- |
| [ETL-SQL Best Practices Guide](patterns/etl-sql-best-practices.md) | Script authoring conventions, transactional blocks, transaction safe zones, and coding style. |
| [One-Person Quality Loop](patterns/one-person-quality-loop.md) | Complete local workflow mapping policy, CLI tests, schedules, local reports, and notifications. |
| [Logging and Performance Tuning](patterns/logging-and-performance.md) | Engine logger targets, detail level settings, disk spill thresholds, and performance debug parameters. |
| [ETL-SQL FAQ & Troubleshooting Guide](patterns/faq.md) | Frequently asked questions, common syntax gotchas, and error remediation steps. |
| [ETL-SQL Sample Guide](patterns/sample-guide.md) | Map of the `samples/` directory files covering basics, connectors, and reports. |
