# eng.safe_zones

`eng.safe_zones` lists configured file-system safe zones used by the engine security boundary.

## Query

```sql
SELECT path, is_system_path, resolution
FROM eng.safe_zones
ORDER BY path;
```

## Columns

| Column | Description |
| :--- | :--- |
| `path` | Approved file-system path. |
| `is_system_path` | `TRUE` when the path is classified as a system path. |
| `resolution` | Authorization result for the path. |

## Example

```sql
SELECT path
FROM eng.safe_zones
WHERE is_system_path = FALSE;
```

## References

- [Engine Catalog](README.md)
- [File Operations](../file-operations/README.md)
