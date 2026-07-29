# eng.bundles

`eng.bundles` lists latest published bundle versions known to the active Orchestrator bundle store.

## Query

```sql
SELECT bundle_name, version, entry_path, published_at
FROM eng.bundles
ORDER BY published_at DESC;
```

## Columns

| Column | Description |
| :--- | :--- |
| `bundle_name` | Published bundle name. |
| `version` | Published bundle version. |
| `entry_path` | Entry script path for the bundle. |
| `content_hash` | Content hash recorded for the bundle version. |
| `published_at` | UTC timestamp when the bundle version was published. |
| `publisher` | User or process that published the bundle version. |
| `description` | Bundle description, when provided. |

## Example

```sql
SELECT bundle_name, version, publisher
FROM eng.bundles
WHERE description IS NOT NULL;
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
