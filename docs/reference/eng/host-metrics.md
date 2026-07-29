# eng.host_metrics

`eng.host_metrics` exposes recent Orchestrator host utilization samples from the configured host metrics store.

## Query

```sql
SELECT node_id, captured_at, host_cpu_percent, memory_load_percent
FROM eng.host_metrics
ORDER BY captured_at DESC;
```

## Columns

| Column | Description |
| :--- | :--- |
| `node_id` | Orchestrator node identifier. |
| `captured_at` | UTC timestamp when the sample was captured. |
| `memory_load_percent` | Host memory load percentage. |
| `process_cpu_percent` | Orchestrator process CPU percentage. |
| `host_cpu_percent` | Host CPU percentage. |
| `state_disk_free_mb` | Free disk space for the state store path, in MB. |
| `spill_disk_free_mb` | Free disk space for the spill path, in MB. |

## Example

```sql
SELECT node_id, MAX(host_cpu_percent) AS peak_cpu
FROM eng.host_metrics
GROUP BY node_id;
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
