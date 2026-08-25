# ETL-SQL Documentation

ETL-SQL documentation is organized by how readers use the product: learn the platform, solve a task, look up exact syntax, operate a deployment, or understand implementation decisions.

## Start Here

- [Getting Started](guides/onboarding/getting-started.md) - first script, engine mental model, connections, variables, and common workflows.
- [ETL Recipes](cookbooks/etl/README.md) - complete pipeline examples for extraction, staging, validation, merge, cleanup, and notification.
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

## Page-Ownership Contract

Each documentation type owns a specific user question or surface. Use the table below to decide where content belongs. When two pages cover the same topic, the focused page wins; the hub or guide links out.

| Page type | Template | Owns | Only links to |
| :--- | :--- | :--- | :--- |
| **Reference — function** | [function-reference-template.md](templates/function-reference-template.md) | Signature, parameters, return type, null behavior, remarks, and a copy-pasteable example for one function | Other functions it depends on; statement pages for context |
| **Reference — statement** | [statement-reference-template.md](templates/statement-reference-template.md) | Syntax, semantics, options, examples, guardrails, errors, and references for one statement or clause | Connector pages for dialect notes; the syntax index |
| **Reference — connector** | [connector-reference-template.md](templates/connector-reference-template.md) | Both authentication patterns, mutually exclusive options, security notes, examples, and troubleshooting for one connector | Configuration pages for global timeout defaults |
| **Reference — visual** | [visual-reference-template.md](templates/visual-reference-template.md) | Mappings, options, actions, a copy-pasteable example, common failures, and FAQ for one visual type | Report-SQL guide for multi-visual layout workflow |
| **Reference — CLI command** | [cli-command-reference-template.md](templates/cli-command-reference-template.md) | Synopsis, arguments, options, exit codes, examples, and notes for one CLI command or subcommand | Getting-started guide for workflow context |
| **Reference — configuration** | [configuration-reference-template.md](templates/configuration-reference-template.md) | Every setting in one configuration area with type, default, scope, and security notes | Administration pages for operational procedures |
| **Hub / README** | [hub-template.md](templates/hub-template.md) | Orientation and links for one documentation area | All focused pages within that area; does not restate syntax, options, or settings |
| **Guide** | [guide-template.md](templates/guide-template.md) | Multi-topic workflow with audience, prerequisites, steps, validation, and troubleshooting | Reference pages for individual statement or function details; does not duplicate syntax inventories |
| **Cookbook recipe** | [cookbook-recipe-template.md](templates/cookbook-recipe-template.md) | One self-contained, runnable end-to-end scenario (extract → stage → validate → merge → cleanup → notify) | Guide pages for workflow context; reference pages for syntax |
| **Architecture** | [architecture-template.md](templates/architecture-template.md) | One subsystem model or one cross-cutting decision: purpose, components, contracts, security, extension points, and tests | Focused reference pages for syntax and options; does not restate them |
| **ADR / Decision record** | [decision-record-template.md](templates/decision-record-template.md) | One architectural decision: context, decision, consequences, alternatives, validation | Architecture overview pages; roadmap and changelog for status |
| **Index** | [index-template.md](templates/index-template.md) | Cross-reference locator (e.g., syntax index, task index) | Focused reference pages; does not duplicate their content |

### Migration Rule — `docs/reference/**`

`docs/reference/**` pages are embedded directly into the engine build as the runtime help corpus (CLI `help`, LSP hover tooltips, and autocomplete). Moving or renaming a file changes or removes a help keyword.

When a reference page moves or is renamed:

1. Update the `.csproj` `<EmbeddedResource Include="..." Link="..." />` entry or its glob.
2. Update any `LanguageMetadata` / `LanguageService` keyword mapping that resolves the old filename.
3. Update every inbound link in `docs/`, `snippets/`, and source code comments in the same change.
4. Verify `dotnet build` (embed globs resolve) and `node scripts/audit-syntax-index.js --strict` both pass before merging.

Moving files **within** `functions/**`, `statements/**`, or `connectors/**` is safe (recursive globs cover them). Moving outside those trees requires an explicit glob update.

`guides/**` is **not** build-embedded — those files can be freely split, renamed, or deleted (fix inbound links only).

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
- Every connector page must include syntax, required options, **both** authentication patterns, mutually exclusive options, security notes, examples, and troubleshooting.
- Every statement page must include syntax, semantics, examples, errors or guardrails, and references.
- Every visual page must include mappings, options, actions, a copy-pasteable example, common failures, and a local FAQ.
- Every guide should state its audience, prerequisites, workflow, validation steps, and related reference pages.
- Every cookbook recipe must be self-contained and runnable as-is.

## Build Constraint — Reference Filenames Are Help Keywords

`docs/reference/**` pages are embedded directly into the engine build as the runtime help corpus (CLI `help`, LSP hover tooltips, and autocomplete). This means:

- **Renaming or deleting a reference page changes or removes a help keyword.** If a page must be renamed, update the csproj `Link` (or its glob) and any `LanguageService` keyword mapping in the same change.
- **Moving a page out of its category folder drops it from help.** Moving files *within* `functions/**`, `statements/**`, or `connectors/**` is safe (recursive globs). Moving outside those trees requires a csproj glob update.
- **`guides/**` is NOT build-embedded** — those files can be freely split, renamed, or deleted (fix inbound links only).
- Every restructure step should end green on `dotnet build` (embed globs resolve) and the `audit-syntax-index.js --strict` check.
