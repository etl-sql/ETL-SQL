# Run-Level Quality Assertions (ASSERT JOB)

While column-level `@expect` rules validate individual rows, **`ASSERT JOB`** validates the health of the entire pipeline run. It evaluates batch-level metrics (volume anomalies, null percentages, data freshness, and quarantine ratios) and routes alerts or halts execution before corrupt data affects downstream consumers.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Metric Assertion Types

| Assertion Metric | Description | Example |
| :--- | :--- | :--- |
| `ROW_COUNT` | Compares total batch volume against historical baseline. | `ROW_COUNT WITHIN 0.2 OF HISTORICAL` |
| `NULL_PERCENT(col)` | Asserts the proportion of NULL values in a column is below a threshold. | `NULL_PERCENT(clean_users.Email) < 0.02` |
| `FRESHNESS(col)` | Checks the maximum timestamp in a column against current clock time. | `FRESHNESS(clean_users.UpdatedAt) < '2 HOURS'` |
| `QUARANTINE_PERCENT` | Asserts that diverted bad rows do not exceed an acceptable ratio. | `QUARANTINE_PERCENT < 0.01` |

---

## Example 1: Standard Pipeline Quality Gate

Validate volume stability, freshness, and low quarantine rates before committing or notifying.

```sql
CREATE CONNECTION src AS FLATFILE('data/nightly_orders.csv');

SELECT OrderId, CustomerEmail, OrderDate, Amount
INTO clean_orders
FROM src
ON FAILURE QUARANTINE TO quarantine_orders WITH (RETENTION = '30 DAYS');

-- Run-level assertion gate
ASSERT JOB nightly_orders (
    ROW_COUNT WITHIN 0.25 OF HISTORICAL,
    NULL_PERCENT(clean_orders.CustomerEmail) < 0.01,
    FRESHNESS(clean_orders.OrderDate) < '6 HOURS',
    QUARANTINE_PERCENT < 0.02
)
ON FAILURE NOTIFY alerts_channel
ON CRITICAL_FAILURE THROW;
```

> [!TIP]
> **Warmup Skipping**: `WITHIN ... OF HISTORICAL` automatically skips evaluation for the first 3 runs of a new job until a stable historical baseline is established, preventing false alerts on initial deployment.

---

## Example 2: Inspecting Quality History with SQL

ETL-SQL records run-level metrics in engine metadata views. You can query these directly to monitor health trends over time.

```sql
-- View run-level timing, status, and quarantine percentages
SELECT job_name, status, total_rows, quarantined_rows, quarantine_percent, duration_ms
FROM eng.data_quality_status
WHERE start_time >= DATEADD(DAY, -7, GETDATE())
ORDER BY start_time DESC;

-- View normalized failure counts per rule
SELECT job_name, target_table, column_name, rule, action, failure_count
FROM eng.data_quality_failures
WHERE job_name = 'nightly_orders';
```

---

## Transition-Based Notifications

When `ON FAILURE NOTIFY` is executed under the Orchestrator:
- **First Failure**: Sends an alert notification immediately.
- **Repeated Failures**: Suppressed during the configured re-notify window to prevent alert fatigue.
- **Recovery**: Sends a "Recovery: Job Passed" notification automatically on the first passing run following a failure.

---

## Related Topics

- [Column Quality Rules](column-quality-rules.md) — Row-level validation rules.
- [Automating Quality Gates](automating-quality-gates.md) — Running gates in CI/CD and schedulers.
- [ASSERT JOB Reference](../../reference/statements/session-control/assert-job.md) — Full syntax reference.
