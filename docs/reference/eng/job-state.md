# eng.job_state

`eng.job_state` lists saved Orchestrator job state key/value records, including watermarks and markers created by job-state helpers.

## Query

```sql
SELECT job_name, state_key, state_value, updated_at
FROM eng.job_state
WHERE job_name = 'daily-load';
```

## Columns

| Column | Description |
| :--- | :--- |
| `job_name` | Scheduled job name. |
| `state_key` | State key. |
| `state_value` | State value stored for the key. |
| `updated_at` | UTC timestamp when the state record was last updated. |

## Example

```sql
SELECT job_name, state_key, updated_at
FROM eng.job_state
WHERE state_key LIKE '%watermark%';
```

## References

- [Engine Catalog](README.md)
- [Orchestrator Jobs](../orchestrator-jobs/README.md)
