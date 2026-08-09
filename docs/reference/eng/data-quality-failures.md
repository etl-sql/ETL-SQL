# eng.data_quality_failures

`eng.data_quality_failures` returns normalized, counts-only rule failures for the current run and
Orchestrator history. Failed sample values are never persisted or returned.

## Query

```sql
SELECT run_id, target_table, column_name, rule, action, failure_count
FROM eng.data_quality_failures
WHERE job_name = 'nightly_etl';
```

Each row identifies the run and status plus one target, column, rule, and action. `owner` carries
the declared steward when available, and `source` identifies current, local, or remote history.

## References

- [Data Quality Guide](../../guides/feature-guides/data-quality.md)
- [Engine Catalog](README.md)
