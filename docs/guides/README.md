# ETL-SQL User and Operator Guides

[« Back to Documentation Hub](../README.md)

ETL-SQL documentation follows a Single Responsibility Principle (SRP) approach with focused, task-oriented guides containing practical, copy-pasteable examples.

---

## 1. Onboarding

Get started with the engine, understand the pipeline mental model, and review version migrations.

| Guide | Description |
| :--- | :--- |
| [5-Minute Quickstart](onboarding/QUICKSTART.md) | Verify installation and execute your first zero-dependency pipeline with `MOCKDB`. |
| [Thinking in Pipelines](onboarding/getting-started.md) | Transition from single-database SQL to multi-context data orchestration. |
| [Migration Guide](onboarding/migration-guide.md) | Baseline notes and upgrade checklist for scripts and deployment configs. |

---

## 2. ETL Pipelines & Orchestration

Design resilient, multi-source ingestion and transformation pipelines.

| Guide | Description |
| :--- | :--- |
| [Staged vs. Streaming Ingestion](pipelines/staged-vs-streaming-ingestion.md) | In-memory `#temp` staging vs. direct single-pass streams comparison and tradeoffs. |
| [Modular Scripts & Parameters](pipelines/modular-scripts-and-parameters.md) | Decompose pipelines into sub-scripts using `RUN SCRIPT` with `INPUT` and `OUTPUT` variables. |
| [Parallel Execution](pipelines/parallel-execution.md) | Execute tasks concurrently across worker threads with `PARALLEL(n)` throttling. |
| [DAG Dependencies & Signals](pipelines/dag-dependencies-and-signals.md) | Model complex workflow DAGs, conditional branches, and file trigger signals. |
| [Error Handling, Alerting & Retries](pipelines/error-handling-and-retries.md) | Catch failures with `TRY...CATCH`, dispatch alerts, and configure automated job retries. |
| [Script Resilience & Checkpoints](pipelines/script-resilience-and-checkpoints.md) | Dry runs with `SET WHAT_IF ON`, transaction safety, and label-based session recovery (`--session`/`--resume`). |
| [Pipeline Unit Testing & Mocking](pipelines/pipeline-unit-testing.md) | Write fast, zero-dependency unit tests using `MOCKDB` and `ASSERT`. |

---

## 3. Data Quality & Governance

Ensure data integrity with inline column validation, quarantine triage, and lineage stewardship.

| Guide | Description |
| :--- | :--- |
| [Column Quality Rules](data-quality/column-quality-rules.md) | Declare inline `EXPECT` rules for null checks, regex patterns, ranges, and castability. |
| [Quarantine & Remediation](data-quality/quarantine-and-remediation.md) | Divert bad rows to durable tables, inspect `__dq_*` metadata, and reprocess with `REPLAY QUARANTINE`. |
| [Multi-Row & Cross-Table Rules](data-quality/multi-row-and-cross-table-rules.md) | Deduplicate with `UNIQUE_FIRST BY`, validate cross-table `EXISTS IN`, and enforce tenant boundaries. |
| [Run-Level Assertions (`ASSERT JOB`)](data-quality/run-level-assertions.md) | Assert batch volume against historical baselines, column freshness, and notification transitions. |
| [Automating Quality Gates](data-quality/automating-quality-gates.md) | Run quality checks in GitHub Actions, Task Scheduler, and Cron with `etlsql-policy.json`. |
| [Data Stewardship & Impact Analysis](data-quality/data-stewardship-and-impact.md) | Script-first tags, metadata gap detection, protected data audits, and lineage impact queries. |

---

## 4. Report-SQL & Dashboards

Author interactive analytical dashboards and print-ready paginated reports.

| Guide | Description |
| :--- | :--- |
| [Authoring Dashboards](reporting/authoring-dashboards.md) | Three-tier architecture, pages, containers, layout grid, and dashboard examples. |
| [Report Parameters & Filters](reporting/report-parameters-and-filters.md) | Wire `INPUT` variables, `RELDATE` expressions, dropdown slicers, and numeric sliders. |
| [Cascading Slicers](reporting/cascading-slicers.md) | Configure dependent parent-child filter hierarchies with atomic parameter updates. |
| [Row-Level Security (RLS)](reporting/report-row-level-security.md) | Secure datasets using `@@CURRENT_USER`, `HAS_GROUP()`, and dynamic permission mappings. |
| [Paginated & Print-Ready Reports](reporting/paginated-and-print-reports.md) | Multi-page documents, Letter/A4 layouts, page breaks, table splitting, and deferred runs. |
| [Micro-Charts & KPI Cards](reporting/micro-charts-and-kpis.md) | In-cell sparklines, attainment progress bars, and standalone KPI cards. |
| [Custom Theming & Branding](reporting/custom-theming-and-branding.md) | Global shell branding, CSS overrides, and custom action buttons. |
| [Report Badges & Trust](reporting/report-badges-and-trust.md) | Ownership, stewardship, certification tier, and freshness indicators. |

---

## 5. Operations & Performance

Run, log, and optimize workloads in development and production environments.

| Guide | Description |
| :--- | :--- |
| [Configuring Script Logging](operations/configuring-script-logging.md) | CLI logging flags, directory routing, file rotation, and credential redaction. |
| [Tuning Pipeline Performance](operations/tuning-pipeline-performance.md) | Buffer batch sizing (`--batch-size`), phase metrics (`--perf`), and profiling (`SET PROFILING ON`). |
| [One-Person Quality Loop](patterns/one-person-quality-loop.md) | Complete workstation runbook: workspace policy, CLI validation, local schedules, and reports. |

---

## 6. Tooling & Designers

Work with visual editors, IDE extensions, notebooks, and Web Portal interfaces.

| Guide | Description |
| :--- | :--- |
| [Visual Report Builder Guide](tooling/report-builder.md) | 12-column drag-and-drop WYSIWYG designer, keyboard ergonomics, and bi-directional script sync. |
| [Web Portal User Guide](tooling/portal-user.md) | Browse folders, execute reports, supply parameters, export PDF/CSV, and manage subscriptions. |
| [Catalog Search & Discovery](tooling/catalog-search.md) | Fuzzy, typo-tolerant metadata search across reports, datasets, owners, stewards, and metrics. |
| [ETL-SQL Notebooks (.etlnb)](tooling/notebook-guide.md) | Stateful interactive cell execution in VS Code for data exploration. |
| [VS Code Extension](tooling/vscode-extension.md) | Language Server Protocol (LSP) features: syntax highlighting, inline linting, and execution trees. |

---

## 7. Patterns & Troubleshooting

Operational recipes, sample maps, and domain troubleshooting diagnostics.

| Guide | Description |
| :--- | :--- |
| [Troubleshooting: Syntax & Dialect](patterns/troubleshooting-syntax-and-dialect.md) | Solutions for dialect mismatches (`TOP` vs `LIMIT`), function availability, and polling loops. |
| [Troubleshooting: Connections & Security](patterns/troubleshooting-connections-and-security.md) | Authentication conflicts, `ENC:` credential resolution, `CREATE SETS`, and safe zone limits. |
| [Troubleshooting: Report-SQL](patterns/troubleshooting-reporting.md) | Solutions for `RELDATE` casting errors, Tier 2 traps, cascading slicers, and action bindings. |
| [Troubleshooting: Performance](patterns/troubleshooting-performance.md) | Resolving slow cross-source joins, streaming massive files, and memory spill optimization. |
| [ETL-SQL FAQ](patterns/faq.md) | Searchable Q&A covering connections, security, operations, and scripting. |
| [Sample Guide](patterns/sample-guide.md) | Comprehensive map of 160+ runnable `.etlsql` and `.rptsql` scripts in `/samples/`. |

---

## 8. Contributor Testing

Test execution lanes, golden scenarios, and enterprise certification for codebase contributors.

| Guide | Description |
| :--- | :--- |
| [Test Lanes & Execution](testing/test-lanes-and-execution.md) | PowerShell lane runners (`test-lane.ps1`, `test-smoke.ps1`), pre-push validation, and execution times. |
| [Golden Scenarios & SQL Logic Tests](testing/golden-scenarios-and-slt.md) | Multi-step pipeline golden tests and ANSI SQL compliance testing with SLT. |
| [Enterprise Certification Testing](testing/enterprise-certification-testing.md) | Zero-trust security certification and dual-platform (Windows / Linux) compliance. |

