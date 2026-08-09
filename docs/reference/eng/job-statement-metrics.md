# `eng.job_statement_metrics`

Per-statement measurements for job runs — the run flight recorder. The current session's statements
appear first as `CURRENT_RUN`, followed by persisted rows from previous runs as `HISTORY`.

```sql
-- The slowest statements across recent runs
SELECT job_name, statement, duration_ms, rows_processed
FROM eng.job_statement_metrics
WHERE source = 'HISTORY'
ORDER BY duration_ms DESC
LIMIT 20;

-- What the last failure actually ran
SELECT ordinal, statement, duration_ms, failed
FROM eng.job_statement_metrics
WHERE job_name = 'nightly_load' AND status = 'FAILURE'
ORDER BY run_id DESC, ordinal;
```

## Columns

| Column | Description |
| :--- | :--- |
| `run_id` | Job-history id, or the session id for the current run |
| `job_name` | Job the run belongs to |
| `start_time` / `end_time` | Run boundaries; null for the current run |
| `status` | `RUNNING`, `SUCCESS`, `FAILURE`, … |
| `ordinal` | Position in the run — order by this to read the timeline |
| `statement` | **Normalized** statement text; see below |
| `duration_ms`, `cpu_time_ms` | Wall-clock and CPU. Duration without CPU means it was waiting |
| `rows_processed` | Rows the statement moved |
| `spilled_bytes`, `spill_read_bytes` | Disk spill written and read back |
| `partitions` | Partition passes; more than one means the data did not fit the budget |
| `queue_wait_ms`, `lock_wait_ms` | Time spent waiting rather than working |
| `index_used` | Index chosen, when one was |
| `dq_rows_validated`, `dq_rows_quarantined`, `dq_rows_warned` | Data-quality rows for this statement |
| `dq_validation_ms` | Time inside rule evaluation — what the rules cost here |
| `failed` | The statement the run stopped on |
| `source` | `CURRENT_RUN` (live session) or `HISTORY` (persisted) |

Column names match [`eng.profile`](profile.md), so the same query shape reads either the live
session or durable history.

## Statement text is normalized, never raw

`statement` never contains literal values. String and numeric literals are replaced with `?`, and
comment bodies are dropped, because durable run history is read by operators who are a different
principal from whoever ran the script — the same reason data-quality evidence is counts-only and
never sample values.

Identifiers are preserved, so a statement stays recognisable:

```sql
-- As executed
INSERT INTO Patient (name, dob) SELECT name, dob FROM staging WHERE region = 'EMEA' AND id > 4200

-- As recorded
INSERT INTO Patient (name, dob) SELECT name, dob FROM staging WHERE region = '?' AND id > ?
```

## Retention and volume

Statement detail is the bulk of a run's rows, so it is retained for less time than the run record
itself, and failed runs are kept longer than successes:

| Setting | Default | Effect |
| :--- | ---: | :--- |
| `Orchestrator:MaxStatementsPerRun` | 25 | Statements kept per run — every failed statement, then the slowest |
| `Orchestrator:MaxStatementTextLength` | 512 | Characters of each statement retained |
| `Orchestrator:SuccessfulStatementMetricsRetentionDays` | 7 | How long a successful run's detail is kept |
| `Orchestrator:FailedStatementMetricsRetentionDays` | 30 | How long a failed run's detail is kept |

Detail is also removed whenever the run's history row is pruned, so rows cannot outlive the run they
describe.

## Availability by deployment profile

Readable in every profile. Solo has no Portal, so this table — not the Portal inbox — is how the
smallest deployment reads its own run history, exactly as with
[`eng.job_history`](job-history.md) and [`eng.data_quality_failures`](data-quality-failures.md).
A bare CLI run remains live-only and records nothing durable unless
[`Engine:AuditAdHocRuns`](../../administration/platform/appsettings-reference.md) or `--record` is
set; `CURRENT_RUN` rows still show that session's statements.

## References

- [`eng.profile`](profile.md) — the live, in-session view with the same column names
- [`eng.job_history`](job-history.md)
- [Engine Catalog](README.md)
