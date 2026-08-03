# Engine Catalog

The `eng` schema exposes engine, session, lineage, orchestration, and bundle inspection data as read-only virtual tables. Query these tables with normal `SELECT`, `WHERE`, `JOIN`, `ORDER BY`, and `INTO` syntax instead of row-returning `SHOW` commands.

```sql
SELECT *
FROM eng.connections
ORDER BY connection_name;
```

## Tables

| Table | Purpose |
| :--- | :--- |
| [`eng.bundle_dependencies`](bundle-dependencies.md) | Packaged `RUN SCRIPT` dependency edges for published bundle versions. |
| [`eng.bundle_files`](bundle-files.md) | Files contained in published bundle versions. |
| [`eng.bundles`](bundles.md) | Latest published bundle versions. |
| [`eng.columns`](columns.md) | Column metadata for session tables and connection tables. |
| [`eng.connection_config`](connection-config.md) | Redacted active connection configuration options. |
| [`eng.connections`](connections.md) | Active session connections. |
| [`eng.data_quality_failures`](data-quality-failures.md) | Normalized counts-only failed rules by run and target. |
| [`eng.data_quality_status`](data-quality-status.md) | Current and orchestrated run quality summaries. |
| [`eng.host_metrics`](host-metrics.md) | Recent Orchestrator host utilization samples. |
| [`eng.job_history`](job-history.md) | Orchestrator job execution history. |
| [`eng.job_state`](job-state.md) | Saved Orchestrator job state key/value records. |
| [`eng.jobs`](jobs.md) | Scheduled Orchestrator jobs. |
| [`eng.lineage`](lineage.md) | Current-session table and column lineage events. |
| [`eng.lineage_history`](lineage-history.md) | Durable lineage events across orchestrated runs. |
| [`eng.locks`](locks.md) | Active concurrency and job-throttle locks. |
| [`eng.missing_tags`](missing-tags.md) | Durable lineage targets missing stewardship metadata. |
| [`eng.profile`](profile.md) | Captured execution profiling metrics. |
| [`eng.protected_data`](protected-data.md) | Durable protected-data lineage inventory. |
| [`eng.protected_data_suggestions`](protected-data-suggestions.md) | Reviewable protected-data classifier findings. |
| [`eng.safe_zones`](safe-zones.md) | Configured file-system safe zones. |
| [`eng.sessions`](sessions.md) | Persisted engine sessions. |
| [`eng.stewardship_gaps`](stewardship-gaps.md) | Source-located unmet stewardship requirements that reconcile to component scores. |
| [`eng.stewardship_score`](stewardship-score.md) | Transparent, versioned stewardship component scores by global, job, and table scope. |
| [`eng.tables`](tables.md) | Table names exposed by active connections. |
| [`eng.tags`](tags.md) | Lineage metadata tags. |
| [`eng.data_quality_rules`](data-quality-rules.md) | Current-session `@expect`/`@fail` rules; `eng.data_quality_rules(job)` over a `PORTAL` connection for another job's. |
| [`eng.variables`](variables.md) | Session variables with sensitive values masked. |
| [`eng.version`](version.md) | Engine version metadata. |
| [`eng.views`](views.md) | Session view definitions. |

Portal connections additionally expose the permission-aware tables and table-valued functions in the [Portal `eng.*` catalog](portal-catalog.md).

## References

- [Syntax Index](../../syntax-index.md)
- [Statement Reference](../statements/README.md)
