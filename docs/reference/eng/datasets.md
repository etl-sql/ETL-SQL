# eng.datasets

`eng.datasets` lists the datasets the caller can see: the Portal's dataset registry when the session runs under a Portal, and otherwise the datasets created with `CREATE DATASET` in this session. It is the engine catalog's dataset listing.

## Query

```sql
SELECT name, access_level, refresh_interval, ttl
FROM eng.datasets;
```

## Columns

| Column | Description |
| :--- | :--- |
| `name` | Dataset name, including its `&` prefix. |
| `folder_path` | Folder that owns the dataset; for a session dataset, the running script's folder. |
| `access_level` | `Private`, or the access level the Portal registry records. |
| `row_count` | Rows at the last refresh, from the Portal registry; empty for a session dataset. |
| `last_refresh` | Last refresh time (ISO 8601), from the Portal registry; empty for a session dataset. |
| `is_stale` | `1` when the last refresh is older than the dataset's `TTL` or it was never refreshed. |
| `refresh_interval` | The dataset's `REFRESH EVERY` interval, if any. |
| `ttl` | The dataset's `TTL`, if any. |

## Example

```sql
CREATE DATASET &daily_sales TTL = '1h' AS (SELECT region, SUM(amount) AS total FROM #orders GROUP BY region);

SELECT name, ttl FROM eng.datasets;
```

## References

- [Engine Catalog](README.md)
- [CREATE DATASET](../visuals-reporting/report/dataset.md)
