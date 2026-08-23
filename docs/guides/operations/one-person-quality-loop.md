# One-Person Quality Loop

This operational workflow gives a solo operator a complete source-controlled pipeline with workspace policy enforcement, non-zero quality gates, local scheduling, durable history, and two monitoring reports without requiring the Web Portal.

Moving the same files into an enterprise deployment preserves all rules, tags, assertions, and score definitions unchanged.

---

> **Applies to:** Solo / Workstation, and serves as the Team starting point. Everything here runs locally without a Portal.

## The Quality Loop Workflow

The runnable sample assets live in [`samples/quality-loop`](../../../samples/quality-loop):

- `customer_quality.etlsql` — Staged transform, stewardship tags, `@expect` rules, quarantine routing, and `ASSERT JOB`.
- `etlsql-policy.json` — Required tags, classifier patterns, thresholds, and score weights.
- `register_schedule.etlsql` — Local SQLite Orchestrator schedule and job registration.
- [`data_quality_health.rptsql`](../../../samples/08_Reporting/data_quality_health.rptsql) — Counts-only run status, failures, trends, and freshness report.
- [`stewardship_scorecard.rptsql`](../../../samples/08_Reporting/stewardship_scorecard.rptsql) — Stewardship score and source-located metadata gaps report.

---

## Step 1: Run and Enforce the Pipeline

Execute the quality-controlled pipeline from the command line:

```powershell
etl-sql run samples/quality-loop/customer_quality.etlsql `
  --quality-summary `
  --output-json artifacts/customer-quality.json
```

The command exits with a non-zero exit code if the critical assertion fails or if required tags are missing according to `etlsql-policy.json`.

Inspect metadata directly in SQL:

```sql
SELECT * FROM eng.data_quality_status;
SELECT * FROM eng.data_quality_failures;
SELECT * FROM eng.stewardship_score WHERE scope_type = 'GLOBAL';
SELECT * FROM eng.stewardship_gaps WHERE scope_type = 'GLOBAL';
```

---

## Step 2: Scan Source Schemas for Sensitive Data

Scan files or databases before tagging scripts:

```powershell
etl-sql scan data/customers.parquet --pii --json
```

For database connections, scan the catalog entry:

```powershell
etl-sql scan SHARED:warehouse --pii --table sales.customers --json
```

---

## Step 3: Register Local Schedules and History

Run the local Orchestrator with SQLite and register the schedule:

```powershell
etl-sql run samples/quality-loop/register_schedule.etlsql
```

Runs populate `eng.job_history` and `eng.data_quality_status`.

---

## Step 4: Preview Quality Reports Locally

Host the operator reports using the local Report Player:

```powershell
etl-sql serve samples/08_Reporting/data_quality_health.rptsql
etl-sql serve samples/08_Reporting/stewardship_scorecard.rptsql
```

---

## Step 5: Configure Failure and Recovery Notifications

To receive alerts on transitions, configure a webhook connection and add `ON FAILURE NOTIFY`:

```sql
CREATE CONNECTION quality_alerts AS WEBHOOK(
  URL = 'SECRET:quality_webhook_url',
  FORMAT = 'slack'
);

CREATE NOTIFICATION data_quality_alerts USING quality_alerts
  WITH (DESCRIPTION = 'Customer quality transitions');

ASSERT JOB customer_quality (QUARANTINE_PERCENT < 0.01)
ON FAILURE NOTIFY data_quality_alerts
ON CRITICAL_FAILURE THROW;
```

---

## Related Topics

- [Column Quality Rules](../data-quality/column-quality-rules.md)
- [Run-Level Assertions](../data-quality/run-level-assertions.md)
- [Authoring Dashboards](../reporting/authoring-dashboards.md)
