# Validating Data Quality

Schema checks tell you the *shape* of your data is right. They say nothing about whether the values
are usable — a `VARCHAR` column happily accepts `"not-an-email"`, and an `INT` column happily
accepts `-5` for an age. This guide covers the value-level half: declaring rules on the columns
themselves and deciding what happens when a row breaks one.

For the full rule and clause syntax, see the
[Data Quality Rules reference](../reference/statements/dml/data-quality-rules.md).

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

## Related

- [Data Quality Rules reference](../reference/statements/dml/data-quality-rules.md) — full syntax
- [EXPECT SCHEMA](../reference/statements/ddl/expect-schema.md) — structural validation
- [Data Stewardship and Impact Analysis](data-stewardship-impact.md) — the governance surfaces these tags feed
