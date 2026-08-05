# Validating Data Quality

Schema checks tell you the *shape* of your data is right. They say nothing about whether the values
are usable — a `VARCHAR` column happily accepts `"not-an-email"`, and an `INT` column happily
accepts `-5` for an age. This guide covers the value-level half: declaring rules on the columns
themselves and deciding what happens when a row breaks one.

For the full rule and clause syntax, see the
[Data Quality Rules reference](../reference/statements/dml/data-quality-rules.md).

## Workspace policy

Place `etlsql-policy.json` at the workspace root to share local-first stewardship and quality
defaults through source control. CLI runs search from the script directory toward the filesystem
root and validate the nearest policy before parsing or executing the script. Invalid JSON, unknown
properties/scopes, malformed regular expressions, and inconsistent thresholds fail the run with
the policy path, line, and column.

`SCRIPT` requirements are checked on script metadata, and `COLUMN` requirements are checked on
columns materialized by `SELECT ... INTO`. A missing or empty required tag is a linter error, so the
same `etl-sql run` command used by a workstation task or CI job exits non-zero before it writes the
target. Exclusion globs match the qualified output name, such as `#scratch.*`.

Use the published [JSON Schema](../../schemas/etlsql-policy.schema.json) for editor validation and
the [complete example](../../samples/05_Security_Diagnostics/etlsql-policy.example.json) as a
starting point. Schema version `1.0` supports:

- required tags with `SCRIPT`, `JOB`, `TABLE`, `COLUMN`, `DATASET`, and `REPORT` scopes plus
  exclusion globs;
- named regex patterns that suggest protected-data classifications without changing metadata;
- default warning and failure thresholds for warning, quarantine, null percentage, and freshness.

Local policy can add checks and stricter defaults, but it cannot weaken authoritative organization
policy in an enrolled environment.

## Start with one rule

Rules are declared as tags in a comment on the column they protect:

```sql
SELECT
    Email /* @expect: 'NOT NULL'; */
INTO clean_users
FROM raw_users
ON FAILURE WARN;
```

That is the smallest useful form. When `@fail` is omitted the action defaults to `WARN`, so nothing
is dropped and nothing throws — at the end of the run you get one aggregated warning per rule:

```text
Data quality: 42 row(s) failed rule "NOT NULL" on column 'Email' [WARN]. Sample values: NULL, NULL, NULL
```

The count is exact; the samples are capped at ten. This matters on a ten-million-row load — a
per-row diagnostic would bury the run log.

## Choose what failure means

`@fail` picks one of three actions, and the trailing `ON FAILURE` clause says where those rows go.

```sql
-- Stop the run. Nothing is loaded.
Email /* @expect: 'NOT NULL'; @fail: 'THROW'; */
```

```sql
-- Load everything, but record what was wrong.
Age /* @expect: '>= 0, <= 120'; @fail: 'WARN'; */
```

```sql
-- Load the good rows; set the bad ones aside for a human.
Email /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */
```

`THROW` suits a rule that means *this data is unusable* — a missing primary key, a broken join key.
`WARN` suits a rule you are still calibrating, or one where a few bad rows are acceptable.
`QUARANTINE` is the workhorse for imports: the clean rows land, the broken ones are set aside with
enough context to fix them.

## A realistic import

```sql
import_users:
SELECT
    UserId   /* @expect: 'NOT NULL'; @fail: 'THROW';
                @expect_1: 'UNIQUE'; @fail_1: 'QUARANTINE'; */,
    Email    /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */,
    Age      /* @expect: '>= 0, <= 120'; @fail: 'WARN'; */,
    Region   /* @expect: "IN ('NA','EMEA','APAC')"; @fail: 'QUARANTINE'; */
INTO clean_users
FROM raw_users
ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS')
ON FAILURE WARN TO warning_log_users WITH (RETENTION = '30 DAYS')
ON FAILURE THROW;
```

Several things are worth noticing:

- **`UserId` carries two rules with different actions.** A missing id stops the run — that is a
  broken feed. A *duplicate* id is a data problem, not a feed problem, so those rows are set aside.
  The numbered suffix (`@expect_1` / `@fail_1`) pairs the second rule with its own action.
- **The statement sits under a section label** (`import_users:`). Quarantining statements require
  one; it identifies the re-entry point when you come back to reprocess the rows you fixed.
- **Both capture targets are durable tables, not `#temp`.** A `#temp` target evaporates when the run
  ends, which defeats the purpose — the linter will nudge you about this.
- **Both have a `RETENTION` window.** Quarantine and warn tables grow forever otherwise.

## What lands in the quarantine table

The captured row is the row **as it was read**, not as it was projected — every source column,
including ones you did not select. That is deliberate: you want to fix the cause in the source, and
the projected value alone often is not enough to find it.

Alongside your columns you get the `__dq_*` set: which rule failed, on which column, the offending
value, a readable reason, timestamps, and a stable row id. So triage is just a query:

```sql
-- What is failing most?
SELECT __dq_column, __dq_rule, COUNT(*) AS Failures
FROM quarantine_users
GROUP BY __dq_column, __dq_rule
ORDER BY Failures DESC;

-- Show me today's bad email addresses with their source context
SELECT UserId, Email, SignupSource, __dq_reason
FROM quarantine_users
WHERE __dq_column = 'Email' AND __dq_ts >= CURRENT_DATE;
```

When the run is orchestrator-hosted, the engine also records a replay manifest for each quarantine
target. The manifest captures the job, script, section label, source table, quarantine target, and
input schema fingerprint that the remediation workflow will use. Labeled single-source quarantines
are replayable. Hash joins are also replayable when the observed build-side keys are N:1 for the
run: the quarantine table captures only the probe/source row, and replay re-runs the join against
the current build table. Fan-out joins remain non-replayable because one released probe row could
regenerate sibling output rows that already passed.

To mark a fixed quarantine row ready for replay, edit the captured source columns and set
`__dq_status = 'released'`. The engine treats the other `__dq_*` columns as immutable evidence.
Warn rows are evidence only: their `__dq_status` stays `warned` and cannot enter the quarantine
release/replay lifecycle.

`REPLAY QUARANTINE <table>` resolves the orchestrator manifest, rejects non-replayable quarantine
targets, claims rows by moving them from `released` to `replaying`, builds a replacement source
stream, strips engine-owned `__dq_*` evidence columns, and resumes the recorded section. After the
section completes successfully, claimed rows move to `__dq_status = 'replayed'`.
Orchestrator-hosted replay takes a cluster lock on the quarantine target so concurrent replays
cannot consume the same released set.

If execution fails after rows are claimed, they remain `replaying` and are not automatically
retried. Review the target for partial side effects, then explicitly return them to `released` for
retry or mark them `replayed`. The Portal queue exposes both recovery choices.

The Portal does not treat HTTP `202 Accepted` as completion. Every replay and row-disposition
submission is recorded durably against the quarantine target in orchestrator job state and followed
to a terminal result through `GET /api/data-quality/jobs/{jobId}`. The submitted-work panel shows
the job ID, current state, timestamps, and sanitized terminal error. Queue or row evidence refreshes
after the terminal result, so a successful submission cannot be mistaken for a successful mutation.

Because the record is durable rather than browser-local, a submission stays visible after the tab is
closed and to a second steward looking at the same target — which is what stops two people replaying
the same production load because neither could see the other's job.

Terminal states are `Completed`, `Failed`, `Cancelled`, and `Unknown`. **`Unknown` is not failure.**
It means the service that was running the job no longer has a record of it, normally because it
restarted; the job may well have completed. Treating that as a failure would invite a second replay
of a load that already ran, so the Portal reports it as unknown, stops polling — no further polling
can produce an answer — and points at the job history for the outcome.

Opening **Trend** also shows the rules protecting the job's output columns—including rules that
have never failed—with their `@expect`/`@fail` tag, action, script source, and line. Failure trends
read normalized per-run rule rows from durable history (target, column, rule, action, owner, and
count); the compact legacy history string is used only for older runs that predate normalized rows.

### Reading quarantined rows in Portal

The Portal queue always lists a capture, always offers the `SELECT` you can run against it yourself,
and always replays it where the manifest allows. Reading the **rows** inside the Portal is separate,
because it means the web tier opens the source connection and returns raw captured data. Four things
must all hold, and the queue names the first one that does not:

| Requirement | Why | What you see when it fails |
| :--- | :--- | :--- |
| The capture recorded a governed shared connection behind its target | Provenance is written when the rows are captured. Portal will not work out where a target lives after the fact — that would mean opening a production connection on an inference | *"This capture has no record of a governed shared connection…"* — including every capture written before v0.18.0 |
| `Portal:DataQuality:AllowConnectionPreview` is `true` | Default **off**, so upgrading never silently starts opening production connections from the web tier | *"Connection preview is disabled…"*, naming the setting |
| You hold a grant on that shared connection | Quarantined rows are raw source rows carrying whatever the source carried. The connection ACL exists to say who may read that | *"…it is not a usable entry in the shared connection catalog, or you have no grant on it"* |
| The capture is self-consistent | A manifest whose target names one alias and whose provenance records another is a contradiction; Portal will not pick one | *"This capture is inconsistent…"* — re-run the job to write a fresh capture |

Two consequences are deliberate:

- **Steward access is not enough.** `DataSteward` gets you to the queue; it does not get you the
  data. An operator who wants a steward reading rows grants them the connection as well. The
  alternative — one role that implicitly reads every connection that has ever produced a capture —
  is an authority that accumulates silently and cannot be revoked where it was granted.
- **Session-local (`#temp`) capture targets are never readable**, and say so rather than showing an
  empty grid. The manifest outlives the run; the table does not. Quarantine into a durable table
  for anything you intend to review later.

The connection Portal opens is the one the *capture* recorded, resolved as `SHARED:<alias>` — never
an alias taken from the request — so policy, secret resolution, and redaction apply exactly as they
do to any script using that connection. A missing, disabled, and ungranted connection all give the
same wording on purpose: the catalog does not disclose the existence of connections you cannot use.

Every successful read writes an audit entry (`READ_QUARANTINE_ROWS`) naming the target, the
connection, the status filter, and the row limit. Reading production data is a data-access event,
not a page view. Previews are row-capped and time-bounded, and a capped result is reported as capped
so a truncated grid is never mistaken for the full quarantine set.

Retention is scoped by `__dq_capture_scope`, a stable job or script identity. On a shared capture
target, one writer's retention window cannot prune another writer's rows. Only terminal
dispositions (`warned`, `replayed`, or `discarded`) age out.

## Rules that need to see every row

Most rules judge a row on its own. `UNIQUE` cannot — it has to see the whole result before it knows
whether any given value repeats.

```sql
load_events:
SELECT
    EventId /* @expect: 'UNIQUE_FIRST BY LoadedAt'; @fail: 'QUARANTINE'; */,
    LoadedAt,
    Payload
INTO clean_events
FROM raw_events
ON FAILURE QUARANTINE TO quarantine_events WITH (RETENTION = '14 DAYS');
```

`UNIQUE` on its own quarantines *every* row in a duplicated group — correct when a duplicate means
the feed is broken. `UNIQUE_FIRST BY` / `UNIQUE_LAST BY` instead keep one row per group, which is
what you usually want for a replayed or at-least-once event feed. The `BY` key is required: without
it "first" would depend on scan order, which is not stable.

The cost is one extra write and read of the statement's rows to spill storage. The source itself is
still read exactly once, so this is safe for feeds that cannot be re-read (Kafka, paginated APIs).
Statements without a `UNIQUE` rule never pay it.

## Checks across columns and tables

```sql
-- A relationship check against a dimension table
RegionId /* @expect: 'EXISTS IN dim_region(Id)'; @fail: 'QUARANTINE'; */

-- A predicate over the whole row
StartDate /* @expect: 'EXPR StartDate <= EndDate'; @fail: 'QUARANTINE'; */

-- Uniqueness over a column pair rather than one column
TenantId /* @expect: 'UNIQUE WITH (TenantId, BookingRef)'; @fail: 'QUARANTINE'; */
```

## Two behaviors that surprise people

**NULL skips every rule except `NOT NULL`.** A NULL `Age` does *not* fail `>= 0`. This follows the
SQL `CHECK`-constraint convention: if it were otherwise, every nullable column would fail twice for
the same underlying problem. If NULL is unacceptable, say so:

```sql
Age /* @expect: 'NOT NULL, >= 0'; @fail: 'QUARANTINE'; */
```

**Rules see the projected value.** `UPPER(Email) /* @expect: 'MATCHES ^[A-Z]+@…' */` validates the
uppercased result, not the stored value. Write the rule against what the column actually produces.

## Removing rules is a loud operation

`@expect` and `@fail` live in comments, and comments can be stripped by formatters and migration
tools. To make that failure visible, the checks are symmetric: a `QUARANTINE` rule with no
`ON FAILURE` clause is an error, **and** an `ON FAILURE` clause with no matching rule is equally an
error. If a tool strips your tags, the leftover clause breaks the script instead of quietly
disabling every check.

One gap remains: rules using only `WARN` or `THROW` with no trailing clause have nothing to leave
behind, so they can still be stripped silently. If enforcement matters, route it through an
`ON FAILURE` clause.

## Tracking quality over time

Every run records how many rows were quarantined and warned, plus per-rule failure counts, on the
job's history. Those counts are what make trends visible — a rule that fails twice a week is noise;
the same rule failing on 30% of rows after a source change is an incident. Sample values are never
persisted to history, and values from `@pii`-tagged columns are masked in warnings and logs — they
survive only inside the capture table, which needs the same access controls as its source.

The canonical query surface is `eng.data_quality_status`. It combines the in-flight run with the
configured local Orchestrator history store and keeps the persisted run ID and status unchanged:

```sql
SELECT *
FROM eng.data_quality_status
WHERE job_name = 'nightly_etl'
  AND start_time >= DATEADD(DAY, -7, GETDATE());
```

It returns timing and status, processed/warned/quarantined counts and percentages, the number of
distinct failed rules, the freshest tracked timestamp and its state, and a sanitized error summary.
`source` distinguishes `CURRENT_RUN` from `ORCHESTRATOR`. `OBSERVED` means a freshness timestamp
was collected; `NOT_TRACKED` means the run did not collect one. Threshold-based stale/fresh gates
remain the responsibility of `ASSERT JOB FRESHNESS(...)` because the catalog does not invent a
freshness threshold.

For rule-level drill-down, query the normalized counts-only catalog. It has one row per run,
target, column, rule, and action and never contains failed sample values:

```sql
SELECT run_id, target_table, column_name, rule, action, failure_count
FROM eng.data_quality_failures
WHERE job_name = 'nightly_etl';
```

`eng.job_history` also exposes `rows_warned`, `rows_quarantined`, and the legacy compact
`failed_rule_counts` display field. Use `eng.data_quality_status` and
`eng.data_quality_failures` for automation; they are built from structured persisted records.

To query another Orchestrator without deploying Portal, use an `ORCHESTRATOR` connection. The same
column contract is returned with `source = 'REMOTE_ORCHESTRATOR'`:

```sql
CREATE CONNECTION ProdOrch AS ORCHESTRATOR(
    HOST = 'https://orchestrator.example.test',
    PASSWORD = 'SECRET:prod_orchestrator_key'
);

SELECT * INTO #remote_status FROM ProdOrch.eng.data_quality_status;
SELECT * INTO #remote_failures FROM ProdOrch.eng.data_quality_failures;
```

## Putting a ceiling on the whole run

Column rules judge rows. [`ASSERT JOB`](../reference/statements/session-control/assert-job.md)
judges the *load*, using those same in-stream metrics:

```sql
ASSERT JOB import_csv (
    ROW_COUNT WITHIN 0.2 OF HISTORICAL,
    NULL_PERCENT(clean_users.Email) < 0.02,
    FRESHNESS(clean_users.UpdatedAt) < '2 HOURS',
    QUARANTINE_PERCENT < 0.01
)
ON FAILURE NOTIFY data_quality_alerts
ON CRITICAL_FAILURE THROW;
```

This is the natural companion to quarantining. A handful of quarantined rows is routine; 40% of the
feed suddenly failing means something upstream broke, and that is a run you want stopped and
announced rather than quietly half-loaded. `ROW_COUNT WITHIN 0.2 OF HISTORICAL` catches the other
common failure — a feed that "succeeds" while delivering a fraction of its usual volume.

Historical baselines use the mean of recent completed runs and deliberately **skip** themselves
until the job has enough history (default 3 runs), so a newly deployed job does not alert-storm on
its first execution. `NULL_PERCENT(target.column)` can also use `WITHIN ... OF HISTORICAL` or
`WITHIN n SIGMA OF HISTORICAL`; those baselines come from per-column run metrics saved with job
history. `FRESHNESS` is current-run only and checks the age of the newest timestamp observed in the
named column.

When `ON FAILURE NOTIFY` runs under the orchestrator, the notification is transition-based: the
first failing run notifies, repeated failing runs are suppressed until the configured re-notify
window elapses, and the first passing run after a failure sends a recovery notification. Suppressed
notifications still appear in logs and run diagnostics, so Slack silence does not mean the run
record is silent.

## Running unattended without Portal

For one or two jobs, invoke the CLI directly from the operating-system scheduler. Set the working
directory to the workspace root so `etlsql-policy.json` discovery and relative safe-zone paths are
deterministic. Both `ON CRITICAL_FAILURE THROW` and `FAIL_ON_WARN = TRUE` produce a non-zero process
exit; SMTP and WEBHOOK connections are optional.

Windows Task Scheduler action:

```text
Program/script: C:\Program Files\ETL-SQL\etl-sql.exe
Arguments: run C:\ETL\nightly.etlsql --quality-summary --output-json C:\ETL\evidence\nightly.json
Start in: C:\ETL
```

Cron entry:

```cron
15 2 * * * cd /opt/etl && /opt/etlsql/etl-sql run nightly.etlsql --quality-summary --output-json evidence/nightly.json
```

CI uses the same command and exit code. Preserve the JSON evidence even when the command fails:

```yaml
- name: Run governed ETL
  run: ./etl-sql run pipelines/nightly.etlsql --output-json artifacts/nightly-quality.json
- name: Upload quality evidence
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: nightly-quality
    path: artifacts/nightly-quality.json
```

When several jobs need scheduling, historical baselines, durable recovery-notification state, or a
shared managed SMTP/WEBHOOK catalog, run the local Orchestrator with its default SQLite store. It
does not require Portal. Define source-controlled `CREATE SCHEDULE`/`CREATE JOB` objects, then query
`eng.data_quality_status`, `eng.data_quality_failures`, and `eng.job_history` from the same host.
Successful runs form historical baselines; failed and running rows do not. A configured
`ON FAILURE NOTIFY` uses transition state in that SQLite store to send failure and recovery
notifications, but omitting the clause leaves exit codes, history, baselines, and catalog queries
fully functional.

## Related

- [Data Quality Rules reference](../reference/statements/dml/data-quality-rules.md) — full syntax
- [ASSERT JOB](../reference/statements/session-control/assert-job.md) — run-level metric assertions
- [EXPECT SCHEMA](../reference/statements/ddl/expect-schema.md) — structural validation
- [Data Stewardship and Impact Analysis](data-stewardship-impact.md) — the governance surfaces these tags feed
