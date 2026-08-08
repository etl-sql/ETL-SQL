# `eng.lineage`

Current-session table and column lineage events, including sources, transformations, locations, and metadata.

```sql
SELECT * FROM eng.lineage WHERE target_table = '#orders';
```

Columns: `timestamp`, `operation`, `target_table`, `target_column`, `source_tables`, `source_columns`, `description`, `metadata`, `derived_from_descriptions`, `source_file`, `line`, `column`, `transformation_kind`, `transformation_expression`, `functions_applied`.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
