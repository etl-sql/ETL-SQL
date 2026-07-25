# Data Quality Rules (@expect / @fail / ON FAILURE)

Column-value validation declared inline on SELECT columns as governance tags, with pluggable
failure actions. Rules are ordinary stewardship tags, so they surface everywhere tags already do —
the tag catalog, lineage, and the stewardship read side — and their per-run impact is recorded on
the job's history.

## Syntax

```sql
<section_label>:
SELECT
    <column> /* @expect: '<rule>[, <rule>...]'; @fail: 'THROW' | 'WARN' | 'QUARANTINE'; */
INTO <target>
FROM <source>
ON FAILURE QUARANTINE TO <table> [WITH (RETENTION = '<interval>')]
ON FAILURE WARN [TO <table>] [WITH (RETENTION = '<interval>')]
ON FAILURE THROW;

REPLAY QUARANTINE <table>;
```

A column can carry several independent rule/action pairs by adding a matching integer suffix:
`@expect_1` pairs with `@fail_1`, `@expect_2` with `@fail_2`, and so on. The unsuffixed
`@expect` pairs with the unsuffixed `@fail`.

## Rules

Combine rules on one column with top-level commas. Commas inside a `MATCHES` pattern, an `IN` list,
or an `EXPR` function call are literal.

| Rule | Meaning |
| :--- | :--- |
| `NOT NULL` | Value must not be NULL. The only rule that fails on NULL. |
| `UNIQUE` | Value must not repeat anywhere in the result. Every row in a duplicated group fails. |
| `UNIQUE WITH (<col>, …)` | Uniqueness over the column tuple rather than the single column. |
| `UNIQUE_FIRST BY <expr>` | Keep only the row with the smallest `<expr>` per duplicate group. |
| `UNIQUE_LAST BY <expr>` | Keep only the row with the largest `<expr>` per duplicate group. |
| `MATCHES <regex>` | Value must match the regular expression. |
| `IN (<list>)` | Value must be one of the listed string or numeric literals. |
| `EXISTS IN <table>(<column>)` | Value must exist in the reference table's key column (relationship / FK check). |
| `EXPR <predicate>` | Boolean predicate over the whole projected row, e.g. `EXPR StartDate <= EndDate`. |
| `>=` `<=` `>` `<` `=` | Numeric comparison against a literal bound, e.g. `>= 0`. |

Quote the rule string. Because tag values keep their quotes through the comment layer, a `;`, `,`,
or `@` inside a quoted value is literal — which is what lets regexes and `IN` lists work. To
include the same kind of quote inside the value, double it (SQL style): `'IN (''NA'',''EMEA'')'`.
Backslash escaping is *not* used, so `MATCHES` patterns pass through untouched.

### Rule semantics

- **Rules validate the projected value.** `SELECT UPPER(Email) /* @expect: 'MATCHES …' */`
  validates the uppercased value.
- **NULL skips every rule except `NOT NULL`** (the SQL `CHECK`-constraint convention, matching dbt's
  `accepted_values`). Pair with `NOT NULL` explicitly to reject NULLs — otherwise every nullable
  column would double-fail.
- **String comparisons honor `SET CASE_SENSITIVE`** for `MATCHES`, `IN`, and `EXISTS IN`.
- **Numeric comparisons are decimal** at runtime.
- **`MATCHES` patterns compile with non-backtracking regex.** A per-row user-supplied regex is
  otherwise a denial-of-service vector. Backreferences and lookaround are rejected at lint time.

## Actions

`@fail` selects what happens to a failing row. When `@expect` is present but `@fail` is omitted,
the action defaults to **`WARN`** — fail-safe, not silent.

| Action | Effect |
| :--- | :--- |
| `THROW` | Raises an execution error and aborts the statement. Never takes a `TO` target. |
| `WARN` | The row still reaches the target. One aggregated warning per (column, rule) is emitted at end of stream with the failure count and up to 10 sample values. Optionally captures each failing row to a warn table. |
| `QUARANTINE` | The row is removed from the output and written to the quarantine target. |

The trailing `ON FAILURE` clause supplies the routing target for each action. Up to three blocks
(one per action) may be stacked. `QUARANTINE` **requires** `TO <table>`; `WARN` takes it
optionally (omit for diagnostic-only mode, where the aggregated warning fires but no row is
stored); `THROW` never takes one.

**Validation is symmetric.** A `@fail: 'QUARANTINE'` with no matching `ON FAILURE QUARANTINE TO`
clause is an error, *and* an `ON FAILURE` clause with no matching `@fail` rule is equally an error.
This is deliberate: if a formatter or tool strips the comment tags, the orphaned clause breaks the
script loudly instead of silently disabling enforcement.

## Capture schema

Quarantine and warn targets receive the **pre-projection input row** — every column the statement
read, including ones absent from the SELECT list — plus these engine columns:

| Column | Contents |
| :--- | :--- |
| `__dq_rule` | The rule text that failed. |
| `__dq_column` | The output column the rule was declared on. |
| `__dq_value` | The projected value that failed (masked when the column is `@pii`-tagged). |
| `__dq_reason` | Human-readable failure reason. |
| `__dq_ts` | UTC capture timestamp. |
| `__dq_run_id` | Identifier of the run that captured the row. |
| `__dq_status` | `'quarantined'` or `'warned'`. |
| `__dq_row_id` | Deterministic hash of the row content plus the run id — a stable row identity. |
| `__dq_origin_row_id` | Reserved; always NULL. |
| `__dq_target_written` | Warn tables only: always `1`, confirming the row still reached the main target. |

The pre-projection row is captured rather than the output row because it points stewards at the
*cause* (the source value) rather than the symptom.

## Remediation Replay

`REPLAY QUARANTINE <table>` resolves the orchestrator replay manifest for the current job and
quarantine target, verifies that the target is replayable, reads rows whose `__dq_status` is
`released`, strips engine-owned `__dq_*` evidence columns, and resumes the recorded section label
with those rows substituted for the original source table. Missing manifests and non-replayable
shapes, such as join-source quarantines, fail before replay starts.

Replay leasing and final `released -> replayed` status flips are follow-up v2 work; a failed replay
therefore leaves released rows eligible for retry.

## Requirements and limits

- **A quarantining statement must sit inside a section label** (e.g. `import_users:`). The label
  identifies the re-entry point for remediation.
- **Quarantine targets should be durable.** A `#temp` target is legal for in-script triage but
  evaporates when the run ends; the linter emits an informational diagnostic recommending a durable
  table.
- **Warn tables have no natural pruning**, so the linter suggests `WITH (RETENTION = '30 DAYS')` on
  every `WARN TO` target. Retention accepts `<n> MINUTES|HOURS|DAYS|WEEKS`.
- **`QUARANTINE` is only legal at a sink boundary** — a top-level SELECT, `INSERT … SELECT`, or
  `SELECT … INTO`. On a nested subquery or CTE column it is an error, because it is a filter with a
  side effect that would silently change downstream row counts.
- **`UNIQUE_FIRST` / `UNIQUE_LAST` require an explicit `BY` key.** Source, spill, and parallel order
  are not stable, so "first" would otherwise be non-deterministic. When two rows tie on the order
  key, the surviving row is chosen by a deterministic full-row comparison, so repeated runs over the
  same data keep the same row.
- **UNIQUE rules cost one extra spill write and read** of the statement's stream: the stream is
  materialized once, and both the duplicate-detection pass and the validation pass read from that
  single copy, so the source is never read twice. Statements without a UNIQUE rule never spill for
  data quality.
- **PII values never leave the quarantine table.** Sample values from a `@pii`-tagged column are
  masked in warnings, logs, `__dq_value`, and alert payloads. The full value survives only inside
  the capture table, which needs the same access controls as its source.
- **Known limitation — comment stripping.** `WARN`- and `THROW`-only rules with no trailing
  `ON FAILURE` clause vanish silently if a downstream tool strips SQL comments. Rules routed
  through an `ON FAILURE` clause are protected by the symmetric check above.

## Per-run metrics

Every run records how many rows were quarantined and warned, plus a compact per-rule failure count,
on the job's history record and on the execution result. Sample values are never persisted there —
counts only.

## Examples

```sql
-- Validate a user import, routing failures three different ways
import_users:
SELECT
    UserId   /* @expect: 'NOT NULL'; @fail: 'THROW';
                @expect_1: 'UNIQUE'; @fail_1: 'QUARANTINE'; */,
    Email    /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */,
    Age      /* @expect: '>= 0, <= 120'; @fail: 'WARN'; */,
    Region   /* @expect: "IN ('NA','EMEA','APAC')"; @fail: 'QUARANTINE'; */,
    RegionId /* @expect: 'EXISTS IN dim_region(Id)'; @fail: 'QUARANTINE'; */
INTO clean_users
FROM raw_users
ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS')
ON FAILURE WARN TO warning_log_users WITH (RETENTION = '30 DAYS')
ON FAILURE THROW;
```

```sql
-- Deduplicate an event feed, keeping the earliest row per event
load_events:
SELECT
    EventId /* @expect: 'UNIQUE_FIRST BY LoadedAt'; @fail: 'QUARANTINE'; */,
    LoadedAt,
    Payload
INTO clean_events
FROM raw_events
ON FAILURE QUARANTINE TO quarantine_events WITH (RETENTION = '14 DAYS');
```

```sql
-- Diagnostic-only mode: warn without storing any rows
SELECT
    Amount /* @expect: '>= 0'; @fail: 'WARN'; */,
    Currency
INTO staged_payments
FROM raw_payments
ON FAILURE WARN;
```

```sql
-- Cross-column check and a composite uniqueness key
load_bookings:
SELECT
    TenantId  /* @expect: 'UNIQUE WITH (TenantId, BookingRef)'; @fail: 'QUARANTINE'; */,
    BookingRef,
    StartDate /* @expect: 'EXPR StartDate <= EndDate'; @fail: 'QUARANTINE'; */,
    EndDate
INTO clean_bookings
FROM raw_bookings
ON FAILURE QUARANTINE TO quarantine_bookings WITH (RETENTION = '30 DAYS');
```

## References

- [DML Statements](README.md)
- [SELECT](select.md)
- [INSERT](insert.md)
- [EXPECT SCHEMA](../ddl/expect-schema.md) — structural (schema) validation
- [ASSERT](../session-control/assert.md) — boolean assertions
- [LINEAGE](../session-control/lineage.md) — the governance tag library these rules join
