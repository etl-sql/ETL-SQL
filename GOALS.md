# ETL-SQL Goals

## Vision

ETL-SQL is a script-first, SQL-like language and runtime for building portable data workflows, paginated reports, and dashboards. It should feel familiar to users who know SQL, while remaining explicit that ETL-SQL is an orchestration engine rather than a traditional single-database SQL engine.

The engine coordinates work across heterogeneous data sources, stages data through governed engine context when needed, and provides a source-control-friendly way to automate ETL, reporting, governance, and operational tasks without requiring a visual designer.

## Product Goals

- Provide ETL capabilities comparable to SSIS-style workflows while keeping scripts readable, repeatable, and portable.
- Provide paginated reporting capabilities comparable to SSRS and Crystal Reports through `.rptsql` scripts.
- Provide dashboard and interactive report capabilities comparable to common BI tools while preserving script-first authoring.
- Support common data sources including SQL databases, flat files, structured files, APIs, cloud storage, file transfer endpoints, and reporting portals.
- Make every core action automatable from scripts, CLIs, jobs, or source-controlled artifacts.
- Keep the language approachable for SQL users without binding the project to one vendor dialect.

## Language Goals

- Use SQL-like syntax where it improves familiarity and readability.
- Support full scripting constructs needed for real workflows, including variables, conditionals, loops, transactions, error handling, jobs, reusable scripts, and report definitions.
- Be dialect-aware when interacting with remote systems, and distinguish clearly between engine-context behavior and remote-context behavior.
- Prefer portable engine semantics for transformations that must work across sources.
- Allow native pushdown only when it is explicit, safe, and appropriate for the target connector.
- Keep scripts idempotent by giving users clear patterns for repeatable creates, loads, refreshes, publication, and cleanup.
- Preserve script readability so changes can be reviewed effectively in source control.

## ETL Goals

- Make staged extraction, validation, transformation, and load workflows easy to express.
- Support lineage and metadata tagging as first-class concepts, not afterthoughts.
- Preserve data stewardship context through transformations where practical.
- Provide safe patterns for destructive changes, including validation, transactions, `WHAT_IF`, and rollback workflows.
- Handle small files, operational datasets, and large data workloads through connector pushdown, batching, streaming, pagination, memory thresholds, and spill strategies where appropriate.
- Provide native fuzzy record linkage and deduplication functions to clean low-quality source data prior to ingestion.
- Support clear recovery behavior when a workflow fails partway through.

## Reporting Goals

- Treat reports and dashboards as source-controlled scripts.
- Keep report data prep, visuals, pages, datasets, containers, navigation, filters, and portal operations scriptable.
- Maintain one shared report semantic model across ReportPlayer, Portal, VS Code preview, and generated manifests.
- Support paginated report patterns, dashboard layouts, interactive filtering, saved views, subscriptions, alerts, catalog operations, and portal administration.
- Keep report runtime behavior consistent across hosts, with shared assets maintained from the canonical report runtime source.

## Governance, Quality, and Stewardship Goals

- Treat lineage (`TAG`, `LINEAGE`) and stewardship (`@owner`, `@steward`, `@classification`) as first-class language keywords that are declared inline in script source code.
- Enforce column-level data quality rules (`@expect` / `@fail` tags) directly on data streams, supporting native stream routing (like `QUARANTINE` and `WARN`) without pipeline rebuilds.
- Make compliance verifiable across all environments. The runtime must be able to reject script execution or report publication if declared policies (e.g., "all tables must have an `@owner` classification") are violated.
- Keep data stewardship information fully portable. Lineage and stewardship must be inspectable and queryable via engine-context virtual tables (`eng.*`), regardless of the underlying database engine dialect.

## Security Goals

- Use a zero-trust architecture as a non-negotiable design constraint.
- Ensure all file system access goes through governed path resolution and script immutability checks.
- Prevent scripts from reading or writing protected locations, script source files, system directories, or drive roots.
- Encrypt all ephemeral data spilled to disk (such as temporary table caches) using session-bound cryptographic keys.
- Keep secrets encrypted or redacted in scripts, logs, manifests, generated files, and source-control workflows.
- Avoid logging connection strings, passwords, API keys, encrypted payloads, or token material.
- Require guardrails for destructive database and file operations.
- Sanitize connector and provider exceptions before they cross runtime boundaries.
- Make secure defaults easier than insecure alternatives.

## Developer Experience Goals

- Provide useful CLI behavior for running scripts, validating syntax, generating reports, and automating jobs.
- Provide editor support through language services, syntax highlighting, diagnostics, completion, and documentation lookup.
- Keep help, examples, grammar references, samples, and cookbook recipes aligned with parser and runtime behavior.
- Make error messages actionable without exposing sensitive details.
- Support local infrastructure emulation (like disposable container management via `USE DOCKER`) to make pipelines self-contained and testable in isolated CI environments.
- Preserve compatibility for existing scripts where practical, and document migration paths when breaking changes are necessary.

## Engineering Quality Goals

- Keep the codebase maintainable through clear ownership boundaries between Core, Engine, Connectors, Analysis, Reporting, ReportHosting, host shells, and editor integrations.
- Follow C#, JavaScript, and TypeScript best practices appropriate to each project.
- Prefer immutable AST records and clear contracts for language model types.
- Use dependency-injected logging and context services rather than global logging or direct console output.
- Use async I/O with cancellation for connector and handler operations.
- Keep performance visible during design and implementation, especially for query planning, connector pushdown, memory use, report rendering, and large data movement.
- Maintain focused automated tests for parser, engine, connectors, analysis, reporting, security, samples, and regression scenarios.
- Treat documentation and sample scripts as part of the product surface, not secondary artifacts.

## Observability And Governance Goals

- Provide structured, sanitized logs suitable for debugging and operations.
- Support explain plans, diagnostics, linting, and validation tools that help users understand what a script will do.
- Make lineage, tags, metadata, report dependencies, history, and permissions inspectable.
- Support audit-friendly workflows for data movement, report publication, refreshes, and portal operations.
- Make governance metadata exportable to systems that need it.

## Multi-Tenancy And SaaS Goals

- Enforce hard boundary isolation between untrusted tenant organizations across databases, memory spaces, caches, and jobs.
- Prevent cross-tenant metadata leakage, ensuring lineage maps, PII scans, and schema discovery suggestion engines are tenant-scoped.
- Support isolated connection catalogs, credential mapping, and dedicated encryption keys per tenant.
- Provide secure tenant onboarding and data export without compromising shared infrastructure logs or performance bounds.

## Extensibility Goals

- Keep connector contracts clear enough that new data sources can be added without weakening security boundaries.
- Prefer small, testable extensions over broad rewrites.
- Allow new syntax only when parser support, docs, examples, tests, and runtime behavior can stay aligned.
- Keep host-specific behavior out of shared language semantics.

## Non-Goals

- ETL-SQL is not intended to replace every native SQL engine or implement every vendor-specific feature.
- ETL-SQL is not a visual-designer-first BI product; visual tools may exist, but scripts remain the source of truth.
- ETL-SQL should not bypass permissions, governance, or security controls of source systems.
- ETL-SQL should not make secrets convenient to commit, print, export, or embed in generated artifacts.
- ETL-SQL should not depend on proprietary or commercially restricted third-party components without explicit approval.
- ETL-SQL should not solve scale by assuming unlimited memory in the engine.

## Priority Principles

When goals conflict, use these principles to rank tradeoffs:

1. Security and correctness come before convenience.
2. Script compatibility comes before syntax expansion.
3. Shared report and runtime semantics come before host-specific features.
4. Diagnostics, documentation, and tests come before broad feature claims.
5. Performance claims must be backed by measurable behavior.
6. Small, well-owned changes are preferred over broad rewrites.

## Success Criteria

- Users can build complete ETL, reporting, and dashboard workflows from scripts alone.
- Scripts can be reviewed in source control without exposing secrets.
- Common workflows have working examples, reference documentation, and automated test coverage.
- Security guardrails are enforced by default and covered by tests.
- Report behavior is consistent across supported hosts.
- Large workload behavior is intentional, documented, and observable.
- New language features ship with parser support, runtime behavior, docs, samples, and tests.
