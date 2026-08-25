# ARCHITECTURE Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ETL-SQL Connectors Architecture & Engineering Reference](connectors.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Deployment Profile Architecture](deployment-profiles.md) | **Status:** Approved target architecture; implementation and certification remain incremental |
| [ETL-SQL Engine Architecture](engine.md) | Engineering reference for contributors. Covers the full project dependency graph, what each project owns, the Evaluator statement dispatch loop, `#... |
| [ETL-SQL Expression Evaluation & Type System](expression-evaluation.md) | This document covers the expression evaluator, operator precedence, type system, NULL propagation, function dispatch, and how expressions are evalu... |
| [Grammar State Engine Architecture](grammar-state-engine.md) | The **Grammar State Engine** is a lightweight, non-deterministic state machine used in ETL-SQL for context-aware autocompletion and automated docum... |
| [ETL-SQL Language Server Architecture](language-server.md) | This document describes the internal design of `ETL-SQL-LSP` — the LSP server that powers IDE features (completions, diagnostics, hover, navigation... |
| [ETL-SQL Lineage & Governance Architecture](lineage.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Orchestrator Architecture & Engineering Reference](orchestrator.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Parser & Lexer Deep Dive](parser-lexer.md) | This document is the primary reference for developers adding new statement types, modifying grammar, or debugging parse errors. For the higher-leve... |
| [Architecture: ETL-SQL Portal](portal.md) | The Portal (`ETL-SQL-Portal`) is an ASP.NET Core 10 web application that exposes report execution, snapshot management, subscriptions, and user/gro... |
| [Workload Identity and Machine-to-Machine Security](workload-identity.md) | Federation and token-exchange threat model, policy contract, replay, approvals, and certification evidence. |
| [Architecture: Portal UI — Visual Designer & DAG Visualization](portal-ui.md) | This document is the authoritative strategy reference for the Portal UI initiative (`v0.9.0-portal-ui`). It governs all design, technology, and sco... |
| [ETL-SQL Presentation Layer Architecture](presentation.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Reporting Architecture & Engineering Reference](reporting.md) | This document describes the internal mechanics of the ETL-SQL reporting subsystem — the layer responsible for parsing `.rptsql` files, evaluating t... |
| [ETL-SQL SaaS Tenant Isolation Architecture](saas-tenant-isolation.md) | **Status:** Approved target architecture; Managed Dedicated and Shared SaaS implementation and |
| [ETL-SQL Tenant Portability Architecture](tenant-portability.md) | **Status:** Minimum configuration/artifact bundle and Managed Dedicated SaaS → Enterprise exit shipped; |
| [ETL-SQL TUI Interactive Editor Architecture](tui-editor.md) | This document describes the internal design of the terminal IDE in `ETL-SQL.TUI` — the interactive editor, syntax highlighting, autocomplete, execu... |
| [ETL-SQL VS Code Extension Architecture](vs-code-extension.md) | This document describes the internal design of `etl-sql-vscode` — the TypeScript VS Code extension that provides syntax highlighting, inline diagno... |
| [ETL-SQL Variable Scoping, Procedures & Dynamic Execution](variable-scoping.md) | This document covers how `@variables` are stored and scoped, how procedures and user-defined functions work, and how `RUN SCRIPT` / `EXECUTE` creat... |
