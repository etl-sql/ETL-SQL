# ENG Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [eng.bundle_dependencies](bundle-dependencies.md) | - Engine Catalog |
| [eng.bundle_files](bundle-files.md) | - Engine Catalog |
| [eng.bundles](bundles.md) | - Engine Catalog |
| [eng.columns](columns.md) | - Engine Catalog |
| [eng.connection_config](connection-config.md) | - Engine Catalog |
| [eng.connections](connections.md) | - Engine Catalog |
| [eng.data_quality_failures](data-quality-failures.md) | Orchestrator history. Failed sample values are never persisted or returned. |
| [`eng.data_quality_rules`](data-quality-rules.md) | Data-quality rules captured from `@expect` and `@fail` metadata in the current session. |
| [eng.data_quality_status](data-quality-status.md) | configured Orchestrator history. Qualify it with an `ORCHESTRATOR` connection to query a remote |
| [ENG](eng.md) | - **eng.connections** - Active session connections. |
| [eng.host_metrics](host-metrics.md) | - Engine Catalog |
| [eng.job_history](job-history.md) | - Engine Catalog |
| [`eng.job_statement_metrics`](job-statement-metrics.md) | Per-statement measurements for job runs — the run flight recorder, live session and durable history. |
| [eng.job_state](job-state.md) | - Engine Catalog |
| [eng.jobs](jobs.md) | - Engine Catalog |
| [`eng.lineage_history`](lineage-history.md) | Durable lineage events captured across orchestrated runs. Qualify the schema with an Orchestrator connection to query a remote catalog. |
| [`eng.lineage`](lineage.md) | Current-session table and column lineage events, including sources, transformations, locations, and metadata. |
| [`eng.locks`](locks.md) | Active engine and job-throttle lock records for concurrency diagnostics. |
| [`eng.missing_tags`](missing-tags.md) | Newest durable lineage targets missing required stewardship tags. |
| [Portal `eng.*` Catalog](portal-catalog.md) | A `PORTAL` connection exposes permission-aware administrative tables and table-valued functions under its `eng` schema. Query them from an `EXECUTE... |
| [eng.profile](profile.md) | - Engine Catalog |
| [`eng.protected_data_suggestions`](protected-data-suggestions.md) | Non-authoritative classifier findings for lineage fields that may need protected-data tags. |
| [`eng.protected_data`](protected-data.md) | Durable lineage records identified as PII, PHI, PCI, sensitive, confidential, or restricted. |
| [eng.safe_zones](safe-zones.md) | - Engine Catalog |
| [`eng.sessions`](sessions.md) | Persisted engine sessions and their size, activity, and ownership metadata. |
| [eng.stewardship_gaps](stewardship-gaps.md) | The table contains metadata only. It never stores failed row samples, protected values, connection strings, or credentials. |
| [eng.stewardship_score](stewardship-score.md) | Weights and required-tag rules come from the nearest `etlsql-policy.json`. Without a workspace policy, the standard required tags are `@owner`, `@s... |
| [eng.tables](tables.md) | - Engine Catalog |
| [eng.tags](tags.md) | - Engine Catalog |
| [eng.variables](variables.md) | - Engine Catalog |
| [eng.version](version.md) | - Engine Catalog |
| [eng.views](views.md) | - Engine Catalog |
