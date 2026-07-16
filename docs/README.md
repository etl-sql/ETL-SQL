# ETL-SQL Documentation

ETL-SQL documentation is organized by how readers use the product: learn the platform, solve a task, look up exact syntax, operate a deployment, or understand implementation decisions.

## Start Here

- [Getting Started](guides/getting-started.md) - first script, engine mental model, connections, variables, and common workflows.
- [ETL Recipes](cookbooks/etl-recipes.md) - complete pipeline examples for extraction, staging, validation, merge, cleanup, and notification.
- [Report SQL](guides/report-sql.md) - author `.rptsql` dashboards, visuals, layouts, filters, datasets, and portal publishing flows.
- [Administration](guides/administration.md) - install, configure, secure, back up, monitor, and scale ETL-SQL.
- [Syntax Index](Syntax_Index.md) - central index of statements, functions, connectors, options, visuals, variables, and CLI commands.

## Documentation Sections

- [Guides](guides/README.md) - task-focused user and operator workflows.
- [Reference](reference/README.md) - exact syntax, options, functions, statements, commands, visuals, and configuration.
- [Cookbooks](cookbooks/README.md) - complete runnable examples.
- [Architecture](architecture/README.md) - subsystem design, source boundaries, standards, decisions, and roadmaps.
- [Releases](releases/README.md) - release notes and release note authoring guidance.
- [Templates](templates/README.md) - templates for keeping new documentation consistent.

## Naming Convention

Use lowercase filenames and directories for documentation paths. Use kebab-case for prose pages, and preserve SQL underscores in function and token filenames. Preserve SQL, connector, visual, and function casing in page titles and headings.

Examples:

- File: `reference/functions/conversion/cast.md`
- Title: `# CAST`
- File: `reference/functions/conversion/try_cast.md`
- Title: `# TRY_CAST`
- File: `reference/visuals-reporting/visuals/hbar.md`
- Title: `# HBAR`

Existing uppercase reference filenames should be migrated gradually with link updates. Do not add new uppercase filenames.

## Documentation Quality Bar

- Every reference page should be copy-pasteable.
- Every function page must include syntax, parameters, return type, null behavior, example, and references.
- Every connector page must include syntax, required options, authentication patterns, mutually exclusive options, security notes, examples, and troubleshooting.
- Every statement page must include syntax, semantics, examples, errors or guardrails, and references.
- Every guide should state its audience, prerequisites, workflow, validation steps, and related reference pages.

## Current Migration Status

The reconfigure from `Docs_Legacy/` is still in progress. Use [Documentation Audit](DOCUMENTATION_AUDIT.md) for the active gap list and cleanup order.
