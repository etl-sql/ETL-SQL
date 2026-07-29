# eng.tags

`eng.tags` exposes lineage metadata as one row per tag, including script-level and table/column-level tags.

## Query

```sql
SELECT TargetTable, TargetColumn, TagName, TagValue, Scope
FROM eng.tags
WHERE TagName = 'owner';
```

## Columns

| Column | Description |
| :--- | :--- |
| `TargetTable` | Target table associated with the tag, or `NULL` for script-level tags. |
| `TargetColumn` | Target column associated with the tag, or `NULL` for table/script-level tags. |
| `Operation` | Lineage operation that produced the metadata row. |
| `TagName` | Tag key. |
| `TagValue` | Tag value. |
| `Scope` | Tag scope: `script`, `table`, or `column`. |
| `Line` | Source line for the lineage record, when available. |
| `SourceFile` | Source file for the lineage record, when available. |

## Example

```sql
SELECT TargetTable, TagValue AS owner
FROM eng.tags
WHERE TagName = 'owner'
  AND Scope = 'table';
```

## References

- [Engine Catalog](README.md)
- [Lineage](../statements/session-control/lineage.md)
