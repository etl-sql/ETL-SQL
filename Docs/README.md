# ETL-SQL Documentation Map

This folder contains several kinds of documentation. Use this page as the starting point when deciding where a new topic belongs or where an existing topic should be maintained.

## Find Docs by Goal

Not sure where to start? Pick your goal:

| I want to… | Start here |
| :--- | :--- |
| Move data like SSIS — connections, transforms, loads | [Cookbook.md](Cookbook.md) → [Reference/Data_Connectors.md](Reference/Data_Connectors.md) |
| Build reports and dashboards like SSRS / Power BI | [Report_SQL_Guide.md](Report_SQL_Guide.md) → [Report_Cookbook.md](Report_Cookbook.md) |
| Schedule and orchestrate jobs | [Orchestrators_Guide.md](Orchestrators_Guide.md) → [Pipelines_and_DAGs.md](Pipelines_and_DAGs.md) |
| Generate a starter ETL script from a vendor data spec | [Reference/Spec_Driven_Development.md](Reference/Spec_Driven_Development.md) → [Cookbook.md](Cookbook.md#25-specification-driven-vendor-feed-build) |
| Secure scripts for source control (encrypt secrets, hash policy) | [User_Manual.md](User_Manual.md) § Security → [Reference/Grammar.md](Reference/Grammar.md) § USE PASSWORD |
| Track data lineage and governance tags | [Reference/Lineage.md](Reference/Lineage.md) |
| Troubleshoot install, connectors, or runtime issues | [FAQ.md](FAQ.md) → run `etl-sql doctor` |

---

## Start Here

| Need | Document |
| :--- | :--- |
| Learn the ETL-SQL mental model and core language flow | [User_Manual.md](User_Manual.md) |
| Find runnable production-style ETL examples | [Cookbook.md](Cookbook.md) |
| Build `.rptsql` reports and dashboards | [Report_SQL_Guide.md](Report_SQL_Guide.md) |
| Find runnable reporting examples | [Report_Cookbook.md](Report_Cookbook.md) |
| Browse the shipped sample scripts | [Sample_Guide.md](Sample_Guide.md) |
| Install, configure, and operate services | [Administrators_Guide.md](Administrators_Guide.md) |
| Understand security posture and residual risks | [../SECURITY.md](../SECURITY.md) |

## Reference

Reference docs describe the supported product surface. They should stay factual, current, and example-driven.

| Document | Purpose |
| :--- | :--- |
| [Reference/Grammar.md](Reference/Grammar.md) | Language grammar and statement syntax |
| [Reference/Data_Connectors.md](Reference/Data_Connectors.md) | Connector tokens, options, and authentication patterns |
| [Reference/Standard_Library.md](Reference/Standard_Library.md) | Data types, functions, operators, and system variables |
| [Reference/Specialized_Operations.md](Reference/Specialized_Operations.md) | File operations, email, Docker, profiling, and automation operations |
| [Reference/Lineage.md](Reference/Lineage.md) | Lineage statements, tags, and governance metadata |
| [Reference/RelativeDate_Parameters.md](Reference/RelativeDate_Parameters.md) | Relative date expressions for reports and subscriptions |
| [Reference/Settings.md](Reference/Settings.md) | Configuration settings reference for appsettings.json |
| [Reference/Spec_Driven_Development.md](Reference/Spec_Driven_Development.md) | Workflow guide for generating scripts from specs |
| [data_spec_parser_instructions.md](data_spec_parser_instructions.md) | Prompt instructions payload for parsing spec files |
| [Reference/spec_pipeline.schema.json](Reference/spec_pipeline.schema.json) | JSON contract validated by `etl-sql gen-script` |
| [Syntax_Index.md](Syntax_Index.md) | Cross-reference from syntax tokens to docs and help files |

Start with [Reference/README.md](Reference/README.md) for ownership and maintenance rules for this section.

## Reporting and Portal

| Document | Audience |
| :--- | :--- |
| [Report_SQL_Guide.md](Report_SQL_Guide.md) | Report authors |
| [Report_Cookbook.md](Report_Cookbook.md) | Report authors who want complete examples |
| [ReportPortal_User_Guide.md](ReportPortal_User_Guide.md) | Portal viewers and report subscribers |
| [ReportPortal_Administrators_Guide.md](ReportPortal_Administrators_Guide.md) | Portal administrators |
| [Report_Runtime_Contract.md](Report_Runtime_Contract.md) | Developers changing shared report runtime behavior |

## Operations and Testing

| Document | Purpose |
| :--- | :--- |
| [Testing.md](Testing.md) | Practical commands for local test lanes, including SLT opt-in rules |
| [Strategy/Test_Strategy.md](Strategy/Test_Strategy.md) | Test lane model and cleanup guidance |
| [Orchestrators_Guide.md](Orchestrators_Guide.md) | Job scheduling and orchestration guide |
| [Pipelines_and_DAGs.md](Pipelines_and_DAGs.md) | Pipeline and DAG patterns |
| [Migration_Guide.md](Migration_Guide.md) | Version migration notes |
| [FAQ.md](FAQ.md) | Troubleshooting and common questions |
| [ETL_Notebook_Guide.md](ETL_Notebook_Guide.md) | Notebook file format and usage |

## Developer Architecture

Architecture docs explain how the current source tree works. Interface contracts and class names in these files should be checked against the C# source before editing.

| Area | Document |
| :--- | :--- |
| Engine execution | [Architecture/Engine.md](Architecture/Engine.md) |
| Parser and lexer | [Architecture/ParserLexer.md](Architecture/ParserLexer.md) |
| Expression evaluation | [Architecture/ExpressionEvaluation.md](Architecture/ExpressionEvaluation.md) |
| Variable scoping and dynamic execution | [Architecture/VariableScoping.md](Architecture/VariableScoping.md) |
| Connectors | [Architecture/Connectors.md](Architecture/Connectors.md) |
| Orchestrator | [Architecture/Orchestrator.md](Architecture/Orchestrator.md) |
| Language server | [Architecture/LanguageServer.md](Architecture/LanguageServer.md) |
| Grammar state engine | [Architecture/GrammarStateEngine.md](Architecture/GrammarStateEngine.md) |
| Presentation layer | [Architecture/Presentation.md](Architecture/Presentation.md) |
| TUI editor | [Architecture/TuiEditor.md](Architecture/TuiEditor.md) |
| VS Code extension | [Architecture/VSCodeExtension.md](Architecture/VSCodeExtension.md) |
| Reporting | [Architecture/Reporting.md](Architecture/Reporting.md) |
| Report Portal | [Architecture/ReportPortal.md](Architecture/ReportPortal.md) |

## Engineering Standards

Standards are normative rules for contributors and agents.

| Document | Purpose |
| :--- | :--- |
| [Standards/Connectors_Standards.md](Standards/Connectors_Standards.md) | Connector implementation, security, and testing rules |
| [Standards/Connector_Certification_Matrix.md](Standards/Connector_Certification_Matrix.md) | Per-connector compliance status against the 10 inviolable rules |
| [Standards/Presentation_Standards.md](Standards/Presentation_Standards.md) | Presentation layer and UI behavior rules |

## Strategy and Design History

The `Strategy` folder contains design records, implementation plans, and historical roadmaps. These files are useful for understanding why decisions were made, but they should not be treated as current product reference unless their status says the feature shipped and the content has been reconciled with the source.

Start with [Strategy/README.md](Strategy/README.md) for the current classification and cleanup guidance.

Recommended cleanup direction:

| Category | Action |
| :--- | :--- |
| Implemented design records | Retitle or annotate as "Implemented Design Note" and keep links to the current architecture/reference docs |
| Stale implementation plans | Move to an archive folder or rewrite as current architecture docs |
| Future backlog notes | Keep only if they describe intentional post-0.7 work, and make that status explicit |

## Documentation Maintenance Rules

- Put user tutorials in guides or cookbooks, not architecture docs.
- Put exact syntax in `Reference`, and keep every syntax form backed by parser support.
- Keep `Syntax_Index.md` as a cross-reference inventory, not the primary explanation of a feature.
- Avoid absolute local `file:///` links in release-facing docs; prefer relative links.
- When a strategy document describes shipped work, update its status so it does not read like a future plan.
