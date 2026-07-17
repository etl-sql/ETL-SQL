# Architecture

Architecture documentation explains how ETL-SQL is built and why major implementation decisions were made.

## Current Subsystem References

- [Engine](Engine.md) - parser, evaluator, temp tables, dispatch, pushdown, linting, and scale behavior.
- [Connectors](Connectors.md) - connector contracts, lifecycle, batching, pushdown, security, and troubleshooting.
- [Reporting](Reporting.md) - Report-SQL parsing, manifests, rendering, snapshots, parameters, and report player.
- [Portal](Portal.md) - portal host, auth, APIs, catalog, subscriptions, health checks, and testing.
- [Orchestrator](Orchestrator.md) - scheduler, execution sessions, leases, history store, child process safety, and channels.
- [Language Server](LanguageServer.md) - analysis pipeline, LSP features, metadata, and extension points.
- [VS Code Extension](VSCodeExtension.md) - commands, REPL, result panels, notebooks, previews, and publishing.
- [Presentation](Presentation.md) - CLI, TUI, language server, report runtime, and UI output boundaries.

## Governance And Standards

- [Standards](standards/README.md) - engineering, connector, syntax, dependency, source-boundary, report runtime, help, and snippet standards.
- [Decisions](decisions/README.md) - design records, release evidence, certification notes, and operational decisions.
- [Roadmaps](roadmaps/README.md) - active and historical strategy documents.

