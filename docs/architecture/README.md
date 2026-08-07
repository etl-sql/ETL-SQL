# ARCHITECTURE Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ETL-SQL Connectors Architecture & Engineering Reference](Connectors.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Engine Architecture](Engine.md) | Engineering reference for contributors. Covers the full project dependency graph, what each project owns, the Evaluator statement dispatch loop, `#... |
| [ETL-SQL Expression Evaluation & Type System](ExpressionEvaluation.md) | This document covers the expression evaluator, operator precedence, type system, NULL propagation, function dispatch, and how expressions are evalu... |
| [Grammar State Engine Architecture](GrammarStateEngine.md) | The **Grammar State Engine** is a lightweight, non-deterministic state machine used in ETL-SQL for context-aware autocompletion and automated docum... |
| [ETL-SQL Language Server Architecture](LanguageServer.md) | This document describes the internal design of `ETL-SQL-LSP` — the LSP server that powers IDE features (completions, diagnostics, hover, navigation... |
| [ETL-SQL Lineage & Governance Architecture](Lineage.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Orchestrator Architecture & Engineering Reference](Orchestrator.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Parser & Lexer Deep Dive](ParserLexer.md) | This document is the primary reference for developers adding new statement types, modifying grammar, or debugging parse errors. For the higher-leve... |
| [Architecture: ETL-SQL Portal](Portal.md) | The Portal (`ETL-SQL-Portal`) is an ASP.NET Core 10 web application that exposes report execution, snapshot management, subscriptions, and user/gro... |
| [Architecture: Portal UI — Visual Designer & DAG Visualization](PortalUI.md) | This document is the authoritative strategy reference for the Portal UI initiative (`v0.9.0-portal-ui`). It governs all design, technology, and sco... |
| [ETL-SQL Presentation Layer Architecture](Presentation.md) | **Applies to ETL-SQL 0.18.0** |
| [ETL-SQL Reporting Architecture & Engineering Reference](Reporting.md) | This document describes the internal mechanics of the ETL-SQL reporting subsystem — the layer responsible for parsing `.rptsql` files, evaluating t... |
| [ETL-SQL TUI Interactive Editor Architecture](TuiEditor.md) | This document describes the internal design of the terminal IDE in `ETL-SQL.TUI` — the interactive editor, syntax highlighting, autocomplete, execu... |
| [ETL-SQL VS Code Extension Architecture](VSCodeExtension.md) | This document describes the internal design of `etl-sql-vscode` — the TypeScript VS Code extension that provides syntax highlighting, inline diagno... |
| [ETL-SQL Variable Scoping, Procedures & Dynamic Execution](VariableScoping.md) | This document covers how `@variables` are stored and scoped, how procedures and user-defined functions work, and how `RUN SCRIPT` / `EXECUTE` creat... |
