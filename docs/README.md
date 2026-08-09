# ETL-SQL Documentation

ETL-SQL documentation is organized by how readers use the product: learn the platform, solve a task, look up exact syntax, operate a deployment, or understand implementation decisions.

## Start Here

- [Getting Started](guides/onboarding/getting-started.md) - first script, engine mental model, connections, variables, and common workflows.
- [ETL Recipes](cookbooks/etl-recipes.md) - complete pipeline examples for extraction, staging, validation, merge, cleanup, and notification.
- [Report SQL](guides/feature-guides/report-sql.md) - author `.rptsql` dashboards, visuals, layouts, filters, datasets, and portal publishing flows.
- [Data Stewardship and Impact Analysis](guides/feature-guides/data-stewardship-impact.md) - use lineage tags, Portal stewardship views, and impact analysis before publish or schema changes.
- [Administration](administration/platform/README.md) - install, configure, secure, back up, monitor, and scale ETL-SQL.
- [Task Index](task-index.md) - goal-oriented "how do I…" locator that points each task to the page that shows how.
- [Syntax Index](syntax-index.md) - searchable map of statements, functions, connectors, options, visuals, variables, and CLI commands.

## Documentation Sections

- [Guides](guides/README.md) - task-focused user and operator workflows.
- [Reference](reference/README.md) - exact syntax, options, functions, statements, commands, visuals, and configuration.
- [Cookbooks](cookbooks/README.md) - complete runnable examples.
- [Architecture](architecture/README.md) - subsystem design, source boundaries, standards, decisions, and roadmaps.
- [Releases](releases/README.md) - release notes and release note authoring guidance.
- [Templates](templates/README.md) - templates for keeping new documentation consistent.
- [spec-import](spec-import/) - internal tooling for parsing data specification files into ETL-SQL script stubs. Contains the JSON schema (`spec_pipeline.schema.json`) and agent instructions for spec-driven development.

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

## Build Constraint — Reference Filenames Are Help Keywords

`docs/reference/**` pages are embedded directly into the engine build as the runtime help corpus (CLI `help`, LSP hover tooltips, and autocomplete). This means:

- **Renaming or deleting a reference page changes or removes a help keyword.** If a page must be renamed, update the csproj `Link` (or its glob) and any `LanguageService` keyword mapping in the same change.
- **Moving a page out of its category folder drops it from help.** Moving files *within* `functions/**`, `statements/**`, or `connectors/**` is safe (recursive globs). Moving outside those trees requires a csproj glob update.
- **`guides/**` is NOT build-embedded** — those files can be freely split, renamed, or deleted (fix inbound links only).
- Every restructure step should end green on `dotnet build` (embed globs resolve) and the `audit-syntax-index.js --strict` check.
