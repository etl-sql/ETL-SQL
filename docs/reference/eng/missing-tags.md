# `eng.missing_tags`

Newest durable lineage targets missing required stewardship tags.

```sql
SELECT * FROM eng.missing_tags LIMIT 100;
```

Columns: `target_table`, `target_column`, `missing_tags`, `present_tags`, `run_at`, `job_name`, `script_path`.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
