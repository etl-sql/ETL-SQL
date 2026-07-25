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
[ON FAILURE ALERT <connection>]
[ON CRITICAL_FAILURE THROW];
```

## Predicates

| Predicate | Meaning |
| :--- | :--- |
| `ROW_COUNT <op> <n>` | Rows validated during the run. |
| `NULL_PERCENT(<column>) <op> <n>` | Fraction (0–1) of NULLs observed in that column across the run's sink writes. |
| `QUARANTINE_PERCENT <op> <n>` | Fraction (0–1) of validated rows removed by a `QUARANTINE` action. |
| `WARN_PERCENT <op> <n>` | Fraction (0–1) of validated rows that failed a `WARN` rule. |
| `<metric> WITHIN <fraction> OF HISTORICAL` | The metric must stay within a relative tolerance of its historical baseline. |

Comparison operators are `>=`, `<=`, `>`, `<`, and `=`. Percent metrics are fractions, not
percentages: `0.02` means 2%.

The quarantine and warn metrics are produced by
[data quality rules](../dml/data-quality-rules.md) — `ASSERT JOB` is how you put a
job-level ceiling on what those rules find.

## HISTORICAL baselines

`WITHIN <f> OF HISTORICAL` compares the current run against the **mean of the last N completed
runs** of the same job name, and fails when `|current − baseline| / baseline > f`.

- Only runs that completed successfully form the baseline — a failed or in-flight run is not a
  reference point.
- `N` defaults to 5 (`Engine:DataQuality:HistoryRuns`).
- **Cold start is defined, not accidental.** Below `Engine:DataQuality:MinHistoryRuns` completed
  runs (default 3), the predicate is **skipped with a warning** rather than failed — a job's first
  deployments must not alert-storm.
- A baseline of zero is also skipped: a relative tolerance around zero is undefined.
- Historical baselines require orchestrator run history. In a host that has none (pure engine or
  embedded use), a `HISTORICAL` predicate fails with an actionable message, while every
  collector-backed predicate still evaluates normally.
- `NULL_PERCENT` has no historical baseline in this release — per-column null fractions are not
  persisted per run, so that combination is skipped with a warning. Compare it against a literal
  instead.

## Failure handling

By default a failing assertion is reported (log + run diagnostics) and the script continues.

- **`ON FAILURE ALERT <connection>`** posts a summary through a named connection — typically a
  [`WEBHOOK`](../../connectors/services/webhook.md) to Slack or Teams. The payload carries the job
  name, the failed predicates, and the run counts. **It never carries sample data**, so values from
  a `@pii`-tagged column cannot reach an alerting channel.
- **`ON CRITICAL_FAILURE THROW`** raises an execution error after alerting, failing the run.

Alert delivery has its own policy: if the webhook is unreachable, that is logged and the run
continues. A broken alerting channel never decides whether the job fails — only
`ON CRITICAL_FAILURE THROW` does, and it raises the assertion's failure, not the delivery error.

## Examples

```sql
-- Guard a nightly import: volume drift, a column's null rate, and quarantine rate
ASSERT JOB import_csv (
    ROW_COUNT WITHIN 0.2 OF HISTORICAL,
    NULL_PERCENT(Email) < 0.02,
    QUARANTINE_PERCENT < 0.01
)
ON FAILURE ALERT alerts_webhook
ON CRITICAL_FAILURE THROW;
```

```sql
-- Fail the run outright if the feed came back empty
ASSERT JOB daily_feed (ROW_COUNT > 0) ON CRITICAL_FAILURE THROW;
```

```sql
-- Notify, but do not fail, while a new rule is being calibrated
ASSERT JOB customer_load (WARN_PERCENT < 0.05) ON FAILURE ALERT data_quality_channel;
```

```sql
-- The webhook the ALERT clause routes through
CREATE CONNECTION alerts_webhook AS WEBHOOK(URL = 'SECRET:slack_url', FORMAT = 'slack');
```

## Notes

- The job name is a label for the metrics being asserted and for the historical lookup; it does not
  have to match a scheduled job, but it must match one for `HISTORICAL` to find prior runs.
- `NULL_PERCENT(col)` resolves the column across the run's sink writes. If two sink statements in
  the same run write a column with that name, the assertion fails with an ambiguity error rather
  than reporting a silently-wrong number.
- A metric that was never observed (for example `QUARANTINE_PERCENT` in a run with no sink writes)
  is skipped with a warning rather than asserted against nothing.
- Rows quarantined and warned are persisted on the job's history record each run, which is what
  makes `HISTORICAL` and trend reporting possible.

## References

- [Session Control](README.md)
- [ASSERT](assert.md) — condition-level assertions
- [Data Quality Rules](../dml/data-quality-rules.md) — the column rules that produce these metrics
- [WEBHOOK connector](../../connectors/services/webhook.md) — the usual `ALERT` target
