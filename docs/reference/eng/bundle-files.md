# eng.bundle_files

`eng.bundle_files` lists files contained in published bundle versions.

## Query

```sql
SELECT bundle_name, version, virtual_path, size_bytes
FROM eng.bundle_files
WHERE bundle_name = 'finance-pipelines';
```

## Columns

| Column | Description |
| :--- | :--- |
| `bundle_name` | Published bundle name. |
| `version` | Bundle version containing the file. |
| `virtual_path` | File path inside the bundle. |
| `content_hash` | Content hash recorded for the file. |
| `size_bytes` | File size in bytes. |
| `content_type` | Stored content type for the file. |

## Example

```sql
SELECT virtual_path, content_type
FROM eng.bundle_files
WHERE version = '1.0.0'
ORDER BY virtual_path;
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
