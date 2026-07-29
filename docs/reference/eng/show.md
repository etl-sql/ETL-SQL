# SHOW

`SHOW` is the legacy inspection command family. Prefer querying read-only `eng.*` virtual tables with normal `SELECT`, `WHERE`, `JOIN`, `ORDER BY`, and `INTO` syntax.

## Syntax

```sql
SELECT column_list
FROM eng.table_name
WHERE predicate;
```

## Replacements

- **SHOW CONNECTIONS** - Use `SELECT * FROM eng.connections`.
- **SHOW TABLES** - Use `SELECT * FROM eng.tables`.
- **SHOW VARIABLES** - Use `SELECT * FROM eng.variables`.
- **SHOW VIEWS** - Use `SELECT * FROM eng.views`.
- **SHOW VERSION** - Use `SELECT * FROM eng.version`.
- **SHOW PROFILE** - Use `SELECT * FROM eng.profile`.
- **SHOW JOBS** - Use `SELECT * FROM eng.jobs`.
- **SHOW JOB HISTORY** - Use `SELECT * FROM eng.job_history`.
- **SHOW JOB STATE** - Use `SELECT * FROM eng.job_state`.
- **SHOW HOST METRICS** - Use `SELECT * FROM eng.host_metrics`.
- **SHOW BUNDLES** - Use `SELECT * FROM eng.bundles`.
- **SHOW BUNDLE FILES** - Use `SELECT * FROM eng.bundle_files`.
- **SHOW BUNDLE DEPENDENCIES** - Use `SELECT * FROM eng.bundle_dependencies`.

## Example

```sql
SELECT connection_name, connector_type
FROM eng.connections
ORDER BY connection_name;
```

## References

- [Engine Catalog](README.md)
- [Syntax Index](../../syntax-index.md)
