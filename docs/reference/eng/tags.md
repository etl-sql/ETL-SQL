# eng.tags

`eng.tags` exposes lineage metadata as one row per tag, including script-level and table/column-level tags.

## Query

```sql
SELECT target_table, target_column, tag_name, tag_value, scope
FROM eng.tags
WHERE tag_name = 'owner';
```

## Columns

| Column | Description |
| :--- | :--- |
| `target_table` | Target table associated with the tag, or `NULL` for script-level tags. |
| `target_column` | Target column associated with the tag, or `NULL` for table/script-level tags. |
| `operation` | Lineage operation that produced the metadata row. |
| `tag_name` | Tag key. |
| `tag_value` | Tag value. |
| `scope` | Tag scope: `script`, `table`, or `column`. |
| `line` | Source line for the lineage record, when available. |
| `source_file` | Source file for the lineage record, when available. |

## Example

```sql
SELECT target_table, tag_value AS owner
FROM eng.tags
WHERE tag_name = 'owner'
  AND scope = 'table';
```

## References

- [Engine Catalog](README.md)
- [Lineage](../statements/session-control/lineage.md)
