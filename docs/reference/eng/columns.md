# eng.columns

`eng.columns` exposes column metadata for current session tables and tables discovered through active connections.

## Query

```sql
SELECT table_name, column_name, data_type, is_nullable
FROM eng.columns
WHERE table_name = '#stage';
```

## Columns

| Column | Description |
| :--- | :--- |
| `table_name` | Session table name or `connection.table` name. |
| `connection_name` | Owning connection name, or `NULL` for engine-side tables. |
| `column_name` | Column name. |
| `data_type` | Known column data type, or `UNKNOWN` when the source cannot report it. |
| `is_nullable` | `TRUE` or `FALSE` when known; defaults to `TRUE` for sources without nullability metadata. |
| `tags` | JSON object of lineage tags attached to the column, or an empty string. |

## Example

```sql
SELECT column_name, tags
INTO #tagged_columns
FROM eng.columns
WHERE table_name LIKE 'sales.%'
  AND tags <> '';
```

## References

- [Engine Catalog](README.md)
- [Lineage](../statements/session-control/lineage.md)
