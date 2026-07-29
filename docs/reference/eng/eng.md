# ENG

`eng` is the engine catalog schema for read-only inspection virtual tables. Query `eng.*` tables with normal `SELECT`, `WHERE`, `JOIN`, `ORDER BY`, and `INTO` syntax.

## Syntax

```sql
SELECT column_list
FROM eng.table_name
WHERE predicate;
```

## Common Tables

- **eng.connections** - Active session connections.
- **eng.tables** - Tables exposed by active connections.
- **eng.columns** - Column metadata for session and connection tables.
- **eng.variables** - Session variables with sensitive values masked.
- **eng.views** - Session view definitions.
- **eng.version** - Engine version metadata.
- **eng.profile** - Captured profiling metrics.
- **eng.jobs** - Scheduled Orchestrator jobs.
- **eng.job_history** - Orchestrator job execution history.
- **eng.job_state** - Saved Orchestrator job-state records.
- **eng.host_metrics** - Recent host utilization samples.
- **eng.bundles** - Latest published bundle versions.
- **eng.bundle_files** - Files contained in published bundle versions.
- **eng.bundle_dependencies** - Packaged script dependency edges.

## Example

```sql
SELECT connection_name, connector_type
FROM eng.connections
ORDER BY connection_name;
```

## References

- [Engine Catalog](README.md)
- [Syntax Index](../../syntax-index.md)
