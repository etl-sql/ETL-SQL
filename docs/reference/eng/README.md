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
| [`eng.host_metrics`](host-metrics.md) | Recent Orchestrator host utilization samples. |
| [`eng.job_history`](job-history.md) | Orchestrator job execution history. |
| [`eng.job_state`](job-state.md) | Saved Orchestrator job state key/value records. |
| [`eng.jobs`](jobs.md) | Scheduled Orchestrator jobs. |
| [`eng.profile`](profile.md) | Captured execution profiling metrics. |
| [`eng.safe_zones`](safe-zones.md) | Configured file-system safe zones. |
| [`eng.tables`](tables.md) | Table names exposed by active connections. |
| [`eng.tags`](tags.md) | Lineage metadata tags. |
| [`eng.variables`](variables.md) | Session variables with sensitive values masked. |
| [`eng.version`](version.md) | Engine version metadata. |
| [`eng.views`](views.md) | Session view definitions. |

## References

- [Syntax Index](../../syntax-index.md)
- [Statement Reference](../statements/README.md)
