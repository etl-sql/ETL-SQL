# One-person quality loop

This workflow gives one operator a source-controlled pipeline, policy, non-zero quality gate, local schedule, durable history, and two reports without requiring Portal. Moving the same files into an enterprise deployment does not change their rules, tags, assertions, or score definition.

The runnable files are in [`samples/quality-loop`](../../../samples/quality-loop):

- `customer_quality.etlsql` — staged transform, stewardship tags, `EXPECT` rules, quarantine routing, and `ASSERT JOB`.
- `etlsql-policy.json` — required tags, classifier patterns, thresholds, and visible score weights.
- `register_schedule.etlsql` — local SQLite Orchestrator schedule and job registration.
- [`data_quality_health.rptsql`](../../../samples/08_Reporting/data_quality_health.rptsql) — counts-only run status, failures, trends, and freshness.
- [`stewardship_scorecard.rptsql`](../../../samples/08_Reporting/stewardship_scorecard.rptsql) — transparent component totals and source-located gaps.

> **Applies to:** Solo / Workstation, and useful as the Team starting point. Everything here runs without a Portal.

## Run and enforce the pipeline

From the repository or workspace root:

```powershell
etl-sql run samples/quality-loop/customer_quality.etlsql `
  --quality-summary `
  --output-json artifacts/customer-quality.json
```

The command exits non-zero when the critical assertion fails. It also exits non-zero during linting
when a materialized output column is missing an `@owner`, `@steward`, or other tag required by the
nearest `etlsql-policy.json`. The JSON artifact is versioned and counts-only; it does not contain
failed sample values.

Inspect the same metadata in SQL:

```sql
SELECT * FROM eng.data_quality_status;
SELECT * FROM eng.data_quality_failures;
SELECT * FROM eng.stewardship_score WHERE scope_type = 'GLOBAL';
SELECT * FROM eng.stewardship_gaps WHERE scope_type = 'GLOBAL';
```

## Scan a source schema before writing tags

```powershell
etl-sql scan data/customers.parquet --pii --json
```

For a database, store the connection once in the governed connection catalog, then scan only the requested table:

```powershell
etl-sql scan SHARED:warehouse --pii --table sales.customers --json
```

The scanner accepts no raw connection string and reads schema names rather than row values.

## Add local scheduling and history

Run the default single-node Orchestrator with SQLite, then register the source-controlled job:

```powershell
etl-sql run samples/quality-loop/register_schedule.etlsql
```

Successful and failed runs populate `eng.job_history`, `eng.data_quality_status`, and the lineage catalog used by the stewardship report. Historical assertions skip themselves until enough completed baselines exist, preventing a first-run alert storm.

## Open the reports

Report Player uses the same scripts and `eng.*` contracts:

```powershell
etl-sql serve samples/08_Reporting/data_quality_health.rptsql
etl-sql serve samples/08_Reporting/stewardship_scorecard.rptsql
```

The scripts also run on a local Orchestrator schedule and may be published to Portal unchanged. Portal adds access control and collaboration; it does not recalculate a more favorable score in browser state.

## Optionally notify on failure and recovery

Notifications are not required for enforcement. To enable them, save an SMTP or WEBHOOK connection whose credential is a `SECRET:name` reference, create a notification, and add `ON FAILURE NOTIFY <name>` to `ASSERT JOB`. Under Orchestrator, the first failure notifies, repeated failures are suppressed until the re-notify window, and the first passing run sends recovery.

```sql
CREATE CONNECTION quality_alerts AS WEBHOOK(
  URL = 'SECRET:quality_webhook_url',
  FORMAT = 'slack'
);
CREATE NOTIFICATION data_quality_alerts USING quality_alerts
  WITH (DESCRIPTION = 'Customer quality transitions');

ASSERT JOB customer_quality (QUARANTINE_PERCENT < 0.01)
ON FAILURE NOTIFY data_quality_alerts
ON FAILURE THROW;
```

No alert or history record includes failed values. Quarantine rows are available only through the separately authorized quarantine target.

## References

- [Data Quality](../feature-guides/data-quality.md)
- [Job Orchestration](../../administration/orchestration/README.md)
- [Report-SQL](../feature-guides/report-sql.md)
- [`eng.stewardship_score`](../../reference/eng/stewardship-score.md)
