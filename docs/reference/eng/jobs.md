# eng.jobs

`eng.jobs` lists scheduled Orchestrator jobs from the configured job history store.

## Query

```sql
SELECT name, schedule, last_run, next_run, enabled
FROM eng.jobs
ORDER BY name;
```

## Columns

| Column | Description |
| :--- | :--- |
| `name` | Scheduled job name. |
| `schedule` | Human-readable schedule expression. |
| `last_run` | UTC timestamp of the previous run, when available. |
| `next_run` | UTC timestamp of the next planned run, when available. |
| `script` | Script path or script identifier run by the job. |
| `enabled` | `1` when enabled; `0` when disabled. |

## Example

```sql
SELECT name, next_run
FROM eng.jobs
WHERE enabled = 1
ORDER BY next_run;
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
