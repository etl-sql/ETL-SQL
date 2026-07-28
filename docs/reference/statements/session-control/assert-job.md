# ASSERT JOB

Asserts on the **run's own metrics** — how many rows it processed, what fraction was quarantined,
how many NULLs landed in a column — rather than on a query result. Metrics are collected in-stream
while the run executes, so no re-scan of the target is needed and write-only sinks (Kafka, webhook,
SMTP) are fully supported.

Where [`ASSERT`](assert.md) guards a condition inside the script,
`ASSERT JOB` guards the shape of the load itself.

## Syntax

```sql
ASSERT JOB <job_name> (
  <predicate> [, <predicate> ...]
)
[ON FAILURE NOTIFY <notification>]
[ON CRITICAL_FAILURE THROW];
```

## Predicates

| Predicate | Meaning |
| :--- | :--- |
| `ROW_COUNT <op> <n>` | Rows processed during the run, matching the count persisted to job history. |
| `NULL_PERCENT(<column>) <op> <n>` | Fraction (0–1) of NULLs observed in that column across the run's sink writes. |
| `NULL_PERCENT(<target>.<column>) <op> <n>` | Target-qualified null fraction for scripts that write the same column name to multiple sinks. `#` temp-table prefixes are accepted. |
| `FRESHNESS(<column>) <op> '<interval>'` | Age of the newest observed timestamp in the column. |
| `FRESHNESS(<target>.<column>) <op> '<interval>'` | Target-qualified freshness check. |
| `QUARANTINE_PERCENT <op> <n>` | Fraction (0–1) of validated rows removed by a `QUARANTINE` action. |
| `WARN_PERCENT <op> <n>` | Fraction (0–1) of validated rows that failed a `WARN` rule. |
| `<metric> WITHIN <fraction> OF HISTORICAL` | The metric must stay within a relative tolerance of its historical baseline. |
| `<metric> WITHIN <n> SIGMA OF HISTORICAL` | The metric must stay within `n` standard deviations of its historical baseline. |

Comparison operators are `>=`, `<=`, `>`, `<`, and `=`. Percent metrics are fractions, not
percentages: `0.02` means 2%.

The quarantine and warn metrics are produced by
[data quality rules](../dml/data-quality-rules.md) — `ASSERT JOB` is how you put a
job-level ceiling on what those rules find.

## HISTORICAL baselines

`WITHIN <f> OF HISTORICAL` compares the current run against the **mean of the last N completed
runs** of the same job name, and fails when `|current - baseline| / baseline > f`.

`WITHIN <n> SIGMA OF HISTORICAL` compares against the same recent-run mean, but fails when the
absolute distance from that mean is greater than `n` standard deviations.

- Only runs that completed successfully form the baseline — a failed or in-flight run is not a
  reference point.
- `N` defaults to 5 (`Engine:DataQuality:HistoryRuns`).
- **Cold start is defined, not accidental.** Below `Engine:DataQuality:MinHistoryRuns` completed
  runs (default 3), the predicate is **skipped with a warning** rather than failed — a job's first
  deployments must not alert-storm.
- Sigma predicates require `Engine:DataQuality:MinSigmaHistoryRuns` completed runs (default 10),
  and the history query expands automatically to load at least that many rows.
- A baseline of zero is also skipped: a relative tolerance around zero is undefined.
- Historical baselines require orchestrator run history. In a host that has none (pure engine or
  embedded use), a `HISTORICAL` predicate fails with an actionable message, while every
  collector-backed predicate still evaluates normally.
- `NULL_PERCENT` history is target-aware and uses per-column metrics persisted with successful job
  history rows.
- `FRESHNESS` is a current-run predicate only; it does not support historical baselines.

## Failure handling

By default a failing assertion is reported (log + run diagnostics) and the script continues.

- **`ON FAILURE NOTIFY <notification>`** posts a summary through a named Orchestrator notification.
  The notification resolves to its configured connection alias and optional recipient at dispatch
  time. The payload carries the job name, the failed predicates, and the run counts. **It never
  carries sample data**, so values from a `@pii`-tagged column cannot reach an alerting channel.
- **`ON CRITICAL_FAILURE THROW`** raises an execution error after alerting, failing the run.

When orchestrator job state is available, notifications are transition-based per job/assertion signature:

- pass → fail sends a failure notification.
- fail → fail suppresses the repeated notification until `Engine:DataQuality:AlertRealertHours` elapses
  (default 24). The suppression is still logged and written to run diagnostics.
- fail → pass sends a recovery notification.

`ON FAILURE NOTIFY` requires an Orchestrator notification catalog. Pure engine hosts without that
catalog raise a clear execution error if notification delivery is needed.

Notification delivery has its own policy: if the destination is unreachable, that is logged and the run
continues. A broken notification channel never decides whether the job fails — only
`ON CRITICAL_FAILURE THROW` does, and it raises the assertion's failure, not the delivery error.

## Examples

```sql
-- Guard a nightly import: volume drift, a column's null rate, and quarantine rate
ASSERT JOB import_csv (
    ROW_COUNT WITHIN 0.2 OF HISTORICAL,
    NULL_PERCENT(clean_users.Email) < 0.02,
    QUARANTINE_PERCENT < 0.01
)
ON FAILURE NOTIFY data_quality_alerts
ON CRITICAL_FAILURE THROW;
```

```sql
-- Use per-column history and sigma bands for a high-variance feed
ASSERT JOB import_csv (
    NULL_PERCENT(#clean_users.Email) WITHIN 2 SIGMA OF HISTORICAL,
    FRESHNESS(clean_users.UpdatedAt) < '2 HOURS'
)
ON FAILURE NOTIFY data_quality_alerts
ON CRITICAL_FAILURE THROW;
```

```sql
-- Fail the run outright if the feed came back empty
ASSERT JOB daily_feed (ROW_COUNT > 0) ON CRITICAL_FAILURE THROW;
```

```sql
-- Notify, but do not fail, while a new rule is being calibrated
ASSERT JOB customer_load (WARN_PERCENT < 0.05) ON FAILURE NOTIFY data_quality_alerts;
```

```sql
-- The notification the NOTIFY clause routes through
CREATE CONNECTION dq_webhook AS WEBHOOK(URL = 'SECRET:slack_url', FORMAT = 'slack');
CREATE NOTIFICATION data_quality_alerts USING dq_webhook
  WITH (DESCRIPTION = 'Data-quality assertion failures');
```

## Notes

- The job name is a label for the metrics being asserted and for the historical lookup; it does not
  have to match a scheduled job, but it must match one for `HISTORICAL` to find prior runs.
- `NULL_PERCENT(col)` resolves the column across the run's sink writes. If two sink statements in
  the same run write a column with that name, the assertion fails with an ambiguity error rather
  than reporting a silently-wrong number. Use `NULL_PERCENT(target.col)` to disambiguate.
- A metric that was never observed (for example `QUARANTINE_PERCENT` in a run with no sink writes)
  is skipped with a warning rather than asserted against nothing.
- Rows quarantined and warned are persisted on the job's history record each run, which is what
  makes `HISTORICAL` and trend reporting possible.

## References

- [Session Control](README.md)
- [ASSERT](assert.md) — condition-level assertions
- [Data Quality Rules](../dml/data-quality-rules.md) — the column rules that produce these metrics
- [WEBHOOK connector](../../connectors/services/webhook.md) — a common notification destination
