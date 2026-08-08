# `eng.lineage_history`

Durable lineage events captured across orchestrated runs. Qualify the schema with an Orchestrator connection to query a remote catalog.

```sql
SELECT * FROM prod_orch.eng.lineage_history WHERE target_table = 'Orders';
```

Columns: `id`, `run_at`, `job_name`, `target_table`, `target_column`, `source_tables`, `operation`, `tags`, `source_file`, `line`.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
