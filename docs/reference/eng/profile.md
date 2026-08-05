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
| `spill_read_bytes` | Bytes read back from spill files — the other half of `spilled_bytes`. |
| `spill_extents` | Spill extents created. A high count with low bytes means fragmentation, not volume. |
| `partition_passes` | Partition passes performed. More than one means the data did not fit the budget. |
| `aggregate_groups` | Distinct groups produced by aggregation — the cardinality that drives memory. |
| `aggregate_expansion_ratio` | Aggregate output rows over input rows. |
| `sort_spills` | Times a sort spilled to disk. |
| `cpu_time_ms` | CPU milliseconds consumed while the statement ran. |
| `dq_rows_validated` | Rows this statement put through data-quality rule evaluation. |
| `dq_rows_quarantined` | Rows this statement diverted to a quarantine target. |
| `dq_rows_warned` | Rows this statement recorded as warnings. |
| `dq_validation_ms` | Milliseconds spent in rule evaluation and capture for this statement. |

### Reading the large-dataset columns

`spilled_bytes` tells you the engine wrote to disk; **`spill_read_bytes` tells you it had to read it
back**, which is the half that costs time on the critical path. A statement that spills and never
reads is cheaper than one that does both.

`partition_passes` above 1 means the data did not fit the configured budget and the engine took
another pass — usually the single most useful signal that a threshold needs raising or the input
needs narrowing.

`spill_extents` high against modest `spilled_bytes` is fragmentation rather than volume, which
points at batch sizing rather than at memory limits.

`cpu_time_ms` against `duration_ms` separates **slow because it was working** from **slow because it
was waiting**: a statement with high duration and low CPU spent its time on I/O, a lock, or a remote
database, and tuning the engine will not help it.

### What data-quality rules cost

The four `dq_*` columns answer a different question from `eng.data_quality_status`. That view reports
what the rules **found** across a run; these report what they **cost** on one statement — which is
the question you have when a load has slowed down and rules are what changed.

Cost is attributed only to the statement carrying the rules; every other statement reports zero, so
you can read the overhead directly rather than inferring it from a total.

`dq_validation_ms` is measured only while profiling is on. Profiling **defaults to on**, so the
column is normally populated; `SET PROFILE OFF` removes both the measurement and the two timestamp
reads per row that produce it. That is the lever to reach for if you are tuning the row pipeline
itself and want the measurement out of the way.

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
