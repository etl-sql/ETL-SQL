# ETL-SQL Reference Map

Reference docs are the authoritative product surface. They should describe what the parser, evaluator, connectors, report runtime, and portal commands actually support today.

## Documents

| Document | Purpose | Maintenance rule |
| :--- | :--- | :--- |
| [Grammar.md](Grammar.md) | Statement syntax, query grammar, orchestration syntax, and examples | Every syntax form must have a minimal working example and parser support |
| [Data_Connectors.md](Data_Connectors.md) | Connector tokens, options, authentication patterns, and aliases | Include mutually exclusive options and both primary auth patterns where applicable |
| [Standard_Library.md](Standard_Library.md) | Data types, functions, operators, and system variables | Every function needs signature, return type, and copy-pasteable example |
| [Specialized_Operations.md](Specialized_Operations.md) | File/directory operations, email, Docker, profiling, and automation operations | Keep operational commands aligned with security guardrails |
| [Lineage.md](Lineage.md) | Lineage statements, tags, metadata, and governance behavior | Keep examples aligned with current lineage analyzer output |
| [RelativeDate_Parameters.md](RelativeDate_Parameters.md) | Relative date expressions for scripts, reports, and subscriptions | Keep portal/subscription examples synchronized with portal guides |
| [Settings.md](Settings.md) | Configuration settings reference for appsettings.json | Synchronize keys and descriptions with changes to the default appsettings.json file |
| [Service_Accounts.md](Service_Accounts.md) | Non-interactive portal identities, scopes, credentials, and lifecycle operations | Keep endpoints, scope enforcement, and security behavior synchronized with the portal |
| [Spec_Driven_Development.md](Spec_Driven_Development.md) | Workflow guide for generating reviewed starter scripts from vendor data specifications | Keep aligned with current CLI compiler, parser prompt, JSON schema, and Cookbook recipe 25 |
| [spec_pipeline.schema.json](spec_pipeline.schema.json) | Machine-readable JSON contract validated by `etl-sql gen-script` | Synchronize with `PipelineGenerator` model and validation rules |
| [../data_spec_parser_instructions.md](../data_spec_parser_instructions.md) | AI prompt instruction sheet for LLM-assisted specification parsing | Synchronize model JSON format changes with the C# compiler and schema contract |

## Reference Boundaries

- Do not put roadmap promises in reference docs.
- Do not document aliases or old forms that the parser no longer accepts.
- Prefer one canonical syntax form when multiple historical forms existed before release.
- Deprecations must follow [Breaking Change Standards](../Standards/Breaking_Change_Standards.md): warn first, keep compatibility for at least two minor releases, document the replacement, and ship machine-readable diagnostics.
- Cross-link to cookbooks for full workflows, but keep the reference examples small and exact.
- When source behavior changes, update the matching reference page in the same change.
