# eng.version

`eng.version` returns engine version metadata for the active runtime.

## Query

```sql
SELECT component, version, metadata
FROM eng.version;
```

## Columns

| Column | Description |
| :--- | :--- |
| `component` | Runtime component name. |
| `version` | Component version. |
| `metadata` | Additional runtime metadata. |

## Example

```sql
SELECT version
INTO #engine_version
FROM eng.version
WHERE component = 'ETL-SQL Engine';
```

## References

- [Engine Catalog](README.md)
- [Syntax Index](../../syntax-index.md)
