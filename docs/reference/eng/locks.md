# `eng.locks`

Active engine and job-throttle lock records for concurrency diagnostics.

```sql
SELECT * FROM eng.locks ORDER BY acquired_at;
```

Columns: `id`, `process_id`, `job_name`, `acquired_at`, `machine_name`.

## References

- [Engine Catalog](README.md)
