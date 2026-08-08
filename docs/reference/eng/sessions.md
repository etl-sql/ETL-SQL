# `eng.sessions`

Persisted engine sessions and their size, activity, and ownership metadata.

```sql
SELECT * FROM eng.sessions ORDER BY last_modified DESC;
```

Columns: `session_id`, `created`, `last_modified`, `size_mb`, `temp_tables`, `variables`, `last_script`, `user`, `machine`.

## References

- [Engine Catalog](README.md)
