# Data Quality Rules (EXPECT / ON FAILURE)
<!-- ShowDataQualityRulesStatement -->

Column-value validation declared inline on SELECT columns, with pluggable failure actions. Rules
are part of the statement's grammar — a rule decides which rows leave the statement, so it is not
something a comment can hold. They are still published to the tag catalog, lineage, and the
stewardship read side, so a steward sees which rules protect which columns exactly as before, and
their per-run impact is recorded on the job's history.

## Syntax

```sql
<section_label>:
SELECT
    <column> [AS <alias>] EXPECT <rule> [ON FAILURE THROW | WARN | QUARANTINE] [EXPECT ...]
INTO <target>
FROM <source>
ON FAILURE QUARANTINE TO <table> [WITH (RETENTION = '<interval>' [, HANDLING = SCRIPT | STEWARD])]
ON FAILURE WARN [TO <table>] [WITH (RETENTION = '<interval>')]
ON FAILURE THROW;

REPLAY QUARANTINE <table>;
```

A column carries several independent rule/action pairs by repeating the clause:

```sql
SELECT UserId EXPECT NOT NULL ON FAILURE THROW
              EXPECT UNIQUE   ON FAILURE QUARANTINE
```

Rules are grammar, not comments — a rule decides which rows leave the statement, so no formatter or
comment stripper can quietly remove one. Comments keep describing (`@d`, `@owner`, `@pii`); writing
a rule as `/* @expect: … */` is a lint error, because it would look enforced and do nothing.

## Rules

Combine rules on one column with `AND` / `OR`. A comma separates columns in the select list, so it
never separates rules; commas inside an `IN` list or a function call are the list's own.

| Rule | Meaning |
| :--- | :--- |
| `NOT NULL` | Value must not be NULL. The only rule that fails on NULL. |
| `NOT BLANK` | Value must contain a non-whitespace character. Skips NULL — pair with `NOT NULL`. |
| `UNIQUE` | Value must not repeat anywhere in the result. Every row in a duplicated group fails. |
| `UNIQUE WITH (<col>, …)` | Uniqueness over the column tuple rather than the single column. |
| `UNIQUE_FIRST BY <expr>` | Keep only the row with the smallest `<expr>` per duplicate group. |
| `UNIQUE_LAST BY <expr>` | Keep only the row with the largest `<expr>` per duplicate group. |
| `MATCHES '<regex>'` | Value must match the regular expression. The pattern is a quoted string. |
| `NOT MATCHES '<regex>'` | Value must **not** match the regular expression. |
| `IN (<list>)` | Value must be one of the listed string or numeric literals. |
| `NOT IN (<list>)` | Value must be none of them — the placeholders a column should never carry. |
| `EXISTS IN <table>(<column>)` | Value must exist in the reference table's key column (relationship / FK check). |
| `EXISTS WITH (<col>, …) IN <table>(<col>, …)` | The tuple of projected columns must exist as a tuple in the reference table (composite / scoped FK check). |
| `LENGTH BETWEEN <min> AND <max>` | Character count within an inclusive range. |
| `LENGTH >= <n>` (`<=` `>` `<` `=`) | Character count compared against a whole, non-negative bound. |
| `CASTABLE AS <type>` | Value must convert to the type, e.g. `CASTABLE AS DATE`, `CASTABLE AS DECIMAL(18,2)`. |
| `BETWEEN <lower> AND <upper>` | Inclusive range whose bounds are expressions — literals, dates, variables, or function calls. |
| `EXPR <predicate>` | Boolean predicate over the whole projected row, e.g. `EXPR StartDate <= EndDate`. |
| `>=` `<=` `>` `<` `=` | Numeric comparison against a literal bound, e.g. `>= 0`. |

| `<rule1> AND <rule2>` | Logical AND: both sub-rules must pass (e.g. `NOT NULL AND > 0`). |
| `<rule1> OR <rule2>` | Logical OR: at least one sub-rule must pass (e.g. `MATCHES '^A' OR MATCHES '^B'`). |
| `(<rule>)` | Parentheses for grouping and overriding operator precedence. |

Rules are written as ordinary tokens, so there is no outer quoting and no doubled quotes:
`IN ('NA','EMEA')` is written once. The one exception is `MATCHES`, whose pattern is a **string
literal** — a bare regex cannot be tokenized, since `@` would start a variable and the operators
would split it. Inside that literal, normal SQL string rules apply (double a `'` to include one)
and nothing else is escaped, so patterns pass through untouched.

### Compound Rules (AND / OR)

Rules can be composed with logical `AND` and `OR` operators and grouped with parentheses `(...)`:
- **Operator Precedence**: `NOT` > `AND` > `OR`. `AND` binds tighter than `OR`.
- **Reporting granularity**: a top-level `AND` is unrolled into independent rules, so each conjunct
  reports its own failures and appears as its own row in the rule catalog. Parenthesize or use `OR`
  to keep a compound rule together.
- **Parentheses**: Use parentheses to control evaluation order, e.g. `NOT NULL AND (LENGTH BETWEEN 5 AND 10 OR MATCHES '^LEGACY-')`.
- **Three-Valued Logic**: `NULL` values skip all non-`NOT NULL` rules.
- **Internal Keywords**: `BETWEEN <lower> AND <upper>` and `LENGTH BETWEEN <min> AND <max>` consume their own `AND` while parsing their bounds, so it is always the range's separator and never a rule conjunction.

### Rule semantics

- **Rules validate the projected value.** `SELECT UPPER(Email) EXPECT MATCHES '…'`
  validates the uppercased value.
- **NULL skips every rule except `NOT NULL`** (the SQL `CHECK`-constraint convention, matching dbt's
  `accepted_values`). Pair with `NOT NULL` explicitly to reject NULLs — otherwise every nullable
  column would double-fail.
- **String comparisons honor `SET CASE_SENSITIVE`** for `MATCHES`, `IN`, and `EXISTS IN`. Column
  *names* in composite rules always match case-insensitively — the setting governs values, not
  identifiers.
- **`BETWEEN` takes expressions, not just numbers.** Bounds are evaluated per row against the
  projected row and compared with the engine's type-aware comparison, so dates compare as dates and
  a variable or function call is a legal bound: `BETWEEN DATEADD(DAY, -30, @RunDate) AND @RunDate`.
  A NULL bound makes the range unknown and the rule skips the row, as SQL's own `BETWEEN` does — a
  rule that failed every row because a variable was unset would report the data as broken when the
  script is. The bare `>=`/`<=` comparison rules remain literal-only and decimal.
- **`CASTABLE AS` uses the engine's own conversion**, the one behind `TRY_CAST`, so the rule and a
  later cast of the same value always agree. A declared width is checked on top of it —
  `DECIMAL(18,2)` allows 16 digits before the point and 2 after, `VARCHAR(50)` at most 50
  characters — because the shared converter ignores widths and an unchecked one would read as a
  constraint while verifying nothing. A type the engine has no conversion for is a parse error, not
  a rule that accepts everything.
- **`LENGTH` measures the rendered value**, matching the `LEN` function — so a number is measured
  as it would print. Every form lowers onto one inclusive range (`LENGTH > 5` becomes a minimum of
  6), and a range no value can satisfy (`LENGTH BETWEEN 10 AND 5`, `LENGTH < 0`) is a parse error
  rather than a rule that quarantines the whole table.
- **`EXISTS WITH` pairs its two column lists positionally**, so the reference table's columns need
  not share the source's names: `EXISTS WITH (TenantId, CustomerId) IN dim_customer(Tenant, Id)`.
  The two lists must have the same arity, and a mismatch is a parse error.
- **A composite rule naming a column the statement does not project is an error.** Row lookup by
  name yields NULL for an absent column, and a NULL key part skips the rule — so a typo would
  otherwise produce a rule that reports clean because it never ran. This applies to `UNIQUE WITH`
  as well as `EXISTS WITH`.
- **Numeric comparisons are decimal** at runtime.
- **`MATCHES` patterns compile with non-backtracking regex.** A per-row user-supplied regex is
  otherwise a denial-of-service vector. Backreferences and lookaround are rejected when the script
  is parsed, so a ReDoS-prone pattern never reaches a run.

## Actions

`ON FAILURE` selects what happens to a failing row. When a rule is written with no action,
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

### Who owns a quarantined row — `HANDLING`

`ON FAILURE QUARANTINE TO <table> WITH (HANDLING = SCRIPT | STEWARD)` says what happens to the
diverted rows after they leave the output. `STEWARD` is the default and the behavior described
throughout this page.

| Handling | Rows leave the output | Replay manifest | Portal steward queue | Section label required | `#temp` target |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `STEWARD` (default) | Yes | Written | Yes | Yes | Linter recommends a durable table |
| `SCRIPT` | Yes | No | No | No | Expected |

Use `SCRIPT` when the running script remediates, reroutes, or discards the rows itself — the rows
still carry their `__dq_*` context, so a later statement in the same run can read the capture table
and act on them:

```sql
SELECT CustomerId EXPECT NOT NULL ON FAILURE QUARANTINE, Region
INTO clean_orders
FROM raw_orders
ON FAILURE QUARANTINE TO #needs_default WITH (HANDLING = SCRIPT);

-- Same run: give the rows a default rather than losing them
INSERT INTO clean_orders (CustomerId, Region)
SELECT 0, Region FROM #needs_default WHERE __dq_rule = 'NOT NULL';
```

Nothing is published for a human to act on afterwards, because by the end of the run there is
nothing left to act on — recording a steward-queue item would ask someone to remediate rows the
script already handled. For the same reason `REPLAY QUARANTINE` cannot target a script-handled
capture table: with no manifest, replay fails before it starts.

The section-label and durable-target requirements both exist to serve remediation *after* the run,
so neither applies under `SCRIPT`. Per-run quality metrics are recorded either way — counts, never
sample values.

**Validation is symmetric.** A column electing `ON FAILURE QUARANTINE` with no matching statement
`ON FAILURE QUARANTINE TO` clause is an error, *and* a statement clause no column elects is equally
an error — routing that nothing uses reads as enforcement that is not happening.

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
| `__dq_capture_scope` | Stable job or script identity used to isolate retention on shared targets. |
| `__dq_status` | `'quarantined'` or `'warned'`. |
| `__dq_row_id` | Deterministic hash of the row content plus the run id — a stable row identity. |
| `__dq_origin_row_id` | Reserved; always NULL. |
| `__dq_target_written` | Warn tables only: always `1`, confirming the row still reached the main target. |

The pre-projection row is captured rather than the output row because it points stewards at the
*cause* (the source value) rather than the symptom. For replayable hash-join quarantines, the
captured row is the probe/source row, not the combined joined row; the join is re-executed during
replay against the current build-side table.

## Remediation Replay

`REPLAY QUARANTINE <table>` resolves the orchestrator replay manifest for the current job and
quarantine target, verifies that the target is replayable, claims rows by moving `__dq_status` from
`released` to `replaying`, strips engine-owned `__dq_*` evidence columns, and resumes the recorded
section label with those rows substituted for the original source table or probe source table.
Missing manifests and non-replayable shapes fail before replay starts.

Replayable shapes are:

- **Single-table source** — released rows replace the original source table.
- **Probe-side hash join** — released rows replace the probe/source table when the run observed at
  most one build-side row per probe key (`JoinObservedN1 = true` in the manifest). The build table is
  read again during replay, so steward fixes to dimension/reference data are picked up.

Fan-out joins are non-replayable. The diagnostic reports that join replay requires an observed N:1
join gate.

After the replayed section completes successfully, claimed rows move from `replaying` to
`replayed`. A failed replay leaves rows `replaying` to prevent an automatic duplicate load.
After checking the target for partial side effects, explicitly return those rows to `released` for
retry or mark them `replayed`. Orchestrator-hosted replay takes a cluster lock before claiming rows.

Retention applies only to terminal `warned`, `replayed`, or `discarded` rows in the current
`__dq_capture_scope`. Active `quarantined`, `released`, and `replaying` evidence is retained.

## Requirements and limits

- **A quarantining statement must sit inside a section label** (e.g. `import_users:`). The label
  identifies the re-entry point for remediation. Not required under `HANDLING = SCRIPT`, which has
  no later re-entry.
- **Quarantine targets should be durable.** A `#temp` target evaporates when the run ends; the
  linter emits an informational diagnostic recommending a durable table. Not emitted under
  `HANDLING = SCRIPT`, where a `#temp` target is the expected choice.
- **Warn tables have no natural pruning**, so the linter suggests `WITH (RETENTION = '30 DAYS')` on
  every `WARN TO` target. Retention accepts `<n> MINUTES|HOURS|DAYS|WEEKS`.
- **`QUARANTINE` is only legal at a sink boundary** — a top-level SELECT, `INSERT … SELECT`, or
  `SELECT … INTO`. On a nested subquery or CTE column it is an error, because it is a filter with a
  side effect that would silently change downstream row counts.
- **Join replay requires an observed N:1 hash join.** Non-hash joins and fan-out hash joins are
  captured for steward review but rejected by `REPLAY QUARANTINE` before replay starts.
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
- **Rules survive tooling.** Because a rule is part of the statement rather than a comment, no
  formatter, comment stripper, or copy-paste through another editor can quietly remove enforcement —
  a tool that mangles a rule produces a syntax error instead of a script that silently stops
  checking. Descriptive tags stay strippable by design; that is the difference between the two.

## Per-run metrics

Every run records how many rows were quarantined and warned, plus a compact per-rule failure count,
on the job's history record and on the execution result. Sample values are never persisted there —
counts only.

## Examples

```sql
-- Validate a user import, routing failures three different ways
import_users:
SELECT
    UserId   EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE QUARANTINE,
    Email    EXPECT MATCHES '^[^@]+@[^@]+$' ON FAILURE QUARANTINE,
    Age      EXPECT >= 0 AND <= 120 ON FAILURE WARN,
    Region   EXPECT IN ('NA','EMEA','APAC') ON FAILURE QUARANTINE,
    RegionId EXPECT EXISTS IN dim_region(Id) ON FAILURE QUARANTINE
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
    EventId EXPECT UNIQUE_FIRST BY LoadedAt ON FAILURE QUARANTINE,
    LoadedAt,
    Payload
INTO clean_events
FROM raw_events
ON FAILURE QUARANTINE TO quarantine_events WITH (RETENTION = '14 DAYS');
```

```sql
-- Diagnostic-only mode: warn without storing any rows
SELECT
    Amount EXPECT >= 0 ON FAILURE WARN,
    Currency
INTO staged_payments
FROM raw_payments
ON FAILURE WARN;
```

```sql
-- Cross-column check and a composite uniqueness key
load_bookings:
SELECT
    TenantId  EXPECT UNIQUE WITH (TenantId, BookingRef) ON FAILURE QUARANTINE,
    BookingRef,
    StartDate EXPECT EXPR StartDate <= EndDate ON FAILURE QUARANTINE,
    EndDate
INTO clean_bookings
FROM raw_bookings
ON FAILURE QUARANTINE TO quarantine_bookings WITH (RETENTION = '30 DAYS');
```

```sql
-- Tenant-scoped foreign key. The single-column form would accept a CustomerId that is real but
-- belongs to a different tenant, which is precisely the row this check exists to catch.
load_orders:
SELECT
    TenantId   EXPECT EXISTS WITH (TenantId, CustomerId) IN dim_customer(TenantId, CustomerId) ON FAILURE QUARANTINE,
    CustomerId,
    Amount
INTO clean_orders
FROM raw_orders
ON FAILURE QUARANTINE TO quarantine_orders WITH (RETENTION = '30 DAYS');
```

## References

- [DML Statements](README.md)
- [SELECT](select.md)
- [INSERT](insert.md)
- [EXPECT SCHEMA](../ddl/expect-schema.md) — structural (schema) validation
- [ASSERT](../session-control/assert.md) — boolean assertions
- [LINEAGE](../session-control/lineage.md) — the governance tag library these rules join
