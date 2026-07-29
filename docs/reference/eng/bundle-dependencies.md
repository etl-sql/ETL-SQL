# eng.bundle_dependencies

`eng.bundle_dependencies` lists packaged script dependency edges discovered for published bundle versions.

## Query

```sql
SELECT bundle_name, version, from_path, to_path
FROM eng.bundle_dependencies
WHERE bundle_name = 'finance-pipelines';
```

## Columns

| Column | Description |
| :--- | :--- |
| `bundle_name` | Published bundle name. |
| `version` | Bundle version that owns the dependency edge. |
| `from_path` | Bundle file that references another script. |
| `to_path` | Referenced bundle file path. |

## Example

```sql
SELECT from_path, to_path
INTO #bundle_deps
FROM eng.bundle_dependencies
ORDER BY bundle_name, version, from_path;
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
