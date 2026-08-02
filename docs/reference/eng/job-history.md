# eng.job_history

`eng.job_history` lists Orchestrator job execution history from the configured job history store.

## Query

```sql
SELECT job_name, start_time, end_time, status, rows_processed
FROM eng.job_history
ORDER BY start_time DESC;
```

## Columns

| Column | Description |
| :--- | :--- |
| `id` | Durable job history record identifier. |
| `job_name` | Scheduled job name. |
| `start_time` | UTC timestamp when execution started. |
| `end_time` | UTC timestamp when execution ended, when available. |
| `status` | Execution status. |
| `rows_processed` | Rows processed by the run. |
| `rows_warned` | Rows retained after a WARN rule failure. |
| `rows_quarantined` | Rows removed by a QUARANTINE rule failure. |
| `failed_rule_counts` | Legacy compact counts-only display payload; use `eng.data_quality_failures` for automation. |
| `peak_ram_mb` | Peak memory usage in MB. |
| `cpu_time_s` | CPU time in seconds. |
| `error_message` | Sanitized error message for failed runs. |

## Example

```sql
SELECT job_name, error_message
FROM eng.job_history
WHERE status = 'Failed'
ORDER BY start_time DESC;
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
