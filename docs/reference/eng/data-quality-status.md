# eng.data_quality_status

`eng.data_quality_status` is the canonical counts-only quality summary for the current run and
configured Orchestrator history. Qualify it with an `ORCHESTRATOR` connection to query a remote
service without Portal.

## Query

```sql
SELECT *
FROM eng.data_quality_status
WHERE job_name = 'nightly_etl';

SELECT *
FROM ProdOrch.eng.data_quality_status;
```

## Columns

| Column | Description |
| :--- | :--- |
| `run_id` | Current session ID or durable history ID. |
| `job_name` | Job name when the run is orchestrated. |
| `start_time`, `end_time` | Run timing; `end_time` is null while running. |
| `status` | The current or persisted execution status. |
| `rows_processed` | Rows processed by the run. |
| `rows_warned`, `warn_percent` | Warned rows and percentage of processed rows. |
| `rows_quarantined`, `quarantine_percent` | Quarantined rows and percentage of processed rows. |
| `failed_rule_count` | Number of distinct normalized failed-rule records. |
| `freshest_value_utc` | Newest timestamp collected for a freshness-tracked column. |
| `freshness_state` | `OBSERVED` or `NOT_TRACKED`; threshold evaluation belongs to `ASSERT JOB`. |
| `error_summary` | Secret-redacted terminal error summary. |
| `source` | `CURRENT_RUN`, `ORCHESTRATOR`, or `REMOTE_ORCHESTRATOR`. |

## References

- [Data Quality Guide](../../guides/data-quality.md)
- [Engine Catalog](README.md)
