# eng.profile

`eng.profile` exposes captured execution profiling metrics for statements executed in the current session.

## Query

```sql
SELECT statement, rows_processed, duration_ms, memory_kb
FROM eng.profile
ORDER BY timestamp DESC;
```

## Columns

| Column | Description |
| :--- | :--- |
| `timestamp` | UTC timestamp for the profile metric. |
| `statement` | SQL text associated with the metric. |
| `rows_processed` | Rows processed by the statement. |
| `index_used` | Index selected by the engine, or `--` when none was used. |
| `duration_ms` | Statement duration in milliseconds. |
| `memory_kb` | Memory delta in KB. |
| `spilled_bytes` | Bytes spilled to disk. |
| `subquery_hits` | Subquery cache hit count. |
| `subquery_misses` | Subquery cache miss count. |
| `subquery_spilled_bytes` | Bytes spilled by subquery cache activity. |
| `partitions` | Number of partitions used by the plan. |
| `queue_wait_ms` | Queue wait time in milliseconds. |
| `lock_wait_ms` | Lock wait time in milliseconds. |
| `plan_decisions` | Total optimizer plan decisions recorded for the session. |
| `plan_accepted` | Accepted optimizer decision count. |
| `plan_fallbacks` | Fallback optimizer decision count. |
| `plan_rejected` | Rejected optimizer decision count. |
| `plan_degraded` | Degraded optimizer decision count. |
| `plan_fallback_summary` | Compact summary of fallback decisions. |

## Example

```sql
SELECT statement, duration_ms
FROM eng.profile
WHERE duration_ms > 1000
ORDER BY duration_ms DESC;
```

## References

- [Engine Catalog](README.md)
- [Performance](../performance/performance.md)
