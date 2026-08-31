# Column & Job Data-Quality Rules — Design Specification

> **Status:** ✅ **v1 SHIPPED** on `release/v0.17.0` (2026-07-25). All three slices are implemented:
> **B** the `WEBHOOK` connector, **A** column rules end-to-end, **C** `ASSERT JOB` + `HISTORICAL`.
> User-facing documentation lives at
> [Data Quality Rules](../../reference/statements/dml/data-quality-rules.md),
> [ASSERT JOB](../../reference/statements/session-control/assert-job.md), and the
> [Validating Data Quality](../../guides/feature-guides/data-quality.md) guide — those are authoritative for
> behavior. This document remains the design record: the decisions and their rationale, plus the
> v2 design below. The quarantine replay manifest foundation, `UPDATE`-time disposition
> enforcement, `REPLAY QUARANTINE` source-substitution replay with cluster-lock fencing, the Portal
> steward queue/editor, and the first scale-hardening pass are now built.
>
> **Where the implementation deliberately differs from this spec** (see "As-built deviations" at the
> end for the reasoning): the §6 `JobMetricsCollector` was folded into `DataQualityReport` rather
> than shipped as a second accumulator; `NULL_PERCENT` has no `HISTORICAL` baseline in v1; the
> UNIQUE duplicate-key map uses hash-partitioned spill rather than `ExternalAggregateEngine`; and
> connector-side retention is opt-in per data source, with SQLite support shipped first.
> Rev 2 (2026-07-24): design-review revisions — streaming job-metrics collector replaces the
> post-run scan, DQ metrics are persisted per run, symmetric `ON FAILURE` validation, webhook
> egress policy, UNIQUE spill-once, WARN aggregation, cold-start/seasonality semantics,
> pushdown pinning, durable quarantine targets, docs/LSP in every slice's definition of done,
> and sequencing flipped to B → A → C.
> Rev 3 (2026-07-24): remediation designed — v1 quarantine made replay-ready (pre-projection
> row capture, required section label, `__dq_status`/`__dq_row_id` shipped up front) and the v2
> label-replay workflow specified (orchestrator-store manifest, released-row source substitution,
> current rules re-applied on replay). Join-statement replay deferred to v3, with the direction
> recorded (probe-side provenance gated on observed N:1) and a v2 replayability lint added.
> Rev 4 (2026-07-24): competitive review — v1 grammar gains the most-used rule forms from the
> field (`EXISTS IN` relationships, cross-column `EXPR`, composite `UNIQUE WITH`); NULL semantics
> defined (non-null rules skip NULLs); `MATCHES` hardened against ReDoS; PII masking in warn
> samples and alert payloads; v2 list gains freshness, sigma tolerance, alert-state dedup, and a
> replay lease.
> Rev 5 (2026-07-24): nested-script quarantines — replay restricted to the job's entry script in
> v2.0 (manifest records the full `RUN SCRIPT` call chain for diagnostics); nested replay via
> recorded call stacks + evaluated arguments captured as a future direction.
> Rev 6 (2026-07-25): manifest foundation implemented — orchestrator-hosted quarantines persist
> job/script/section/source/target replay metadata, captured input columns, schema fingerprint, and
> replayability status; join-source quarantines are recorded as non-replayable for v2.
> Rev 7 (2026-07-25): disposition enforcement implemented for engine-side `UPDATE` paths touching
> `__dq_*`: evidence columns are immutable except `__dq_status`, warn status is immutable, and
> quarantine lifecycle transitions are validated.
> Rev 8 (2026-07-25): `REPLAY QUARANTINE` preflight implemented — resolves manifests through the
> existing orchestrator metrics seam, rejects missing/non-replayable manifests, scans released rows,
> and returns a ready summary without mutating data.
> Rev 9 (2026-07-25): single-table source-substitution replay implemented — released rows are
> stripped of `__dq_*` evidence columns, substituted for the recorded source table, and run through
> the existing resume-at-label machinery. Successful replay flips consumed rows to `replayed`, and
> orchestrator-hosted replay is fenced by the cluster-lock store.
> Rev 10 (2026-07-25): v3 probe-side hash-join replay implemented for observed N:1 build keys.
> Join quarantines capture probe/source rows, manifests record `probe-join` provenance and the
> build table, fan-out joins remain non-replayable with a specific N:1 diagnostic, and released
> probe rows replay through the existing source-substitution path.
> **Rev 11 (2026-08-30): surface redesign — breaking, with no compatibility shim.** Three changes
> under one principle: *comments describe, syntax executes; one action vocabulary everywhere.*
> (1) Column rules move **out of comment tags into first-class syntax** — `EXPECT <rule> [ON FAILURE
> <action>]` on the select column — replacing `/* @expect: '…'; @fail: '…'; */` and the numbered
> `@expect_N`/`@fail_N` pairing. (2) `ASSERT JOB` drops `ON CRITICAL_FAILURE` and
> `WITH (FAIL_ON_WARN = …)` in favour of stacked `ON FAILURE <action>` blocks drawn from the same
> action vocabulary the SELECT clause uses. (3) A predicate naming a column no sink in the script
> writes becomes a **lint error** rather than a runtime skip, closing the gap where a typo made an
> assertion pass green. Nothing is deployed against the shipped v1 surface, so the old forms are
> **deleted, not deprecated**. Full rationale and the change inventory: "Rev 11 — surface redesign"
> near the end of this document.

## Goal

Extend the engine's verification surface from **schema** (`EXPECT SCHEMA … ON DRIFT WARN`) and
**boolean assertions** (`ASSERT`) to **column-value** and **job-metric** data quality:

- **Column rules** declared inline on SELECT columns as first-class syntax — `SELECT UserId EXPECT NOT NULL ON FAILURE THROW` — with pluggable fail actions (`THROW` / `WARN` / `QUARANTINE`).
- **Job rules** declared with `ASSERT JOB` over run metrics collected **in-stream during execution** — e.g. `ROW_COUNT WITHIN 0.2 OF HISTORICAL`, `NULL_PERCENT(Email) < 0.02`, `QUARANTINE_PERCENT < 0.01` — with alerting.
- A **webhook** connector so failures can notify Slack / Teams / generic endpoints.

This closes the data-quality half of the stewardship+quality sprint; DQ failures are conceptually
governance findings and are designed to feed the same lineage/stewardship read side the Governance
dashboard already consumes.

**Why syntax, not comment tags (Rev 11):** the `/* @tag: value; */` convention works because those
tags are **descriptive** — strip them and the script still runs and still produces the same rows, so
they are safe to carry into any other SQL tool. `EXPECT` clauses were **imperative**:
`@fail: 'QUARANTINE'` removes rows from the output, so stripping the comment silently changed the
data. The `ON FAILURE` clause they require is not a comment either, so the tag form never actually
bought portability for a rule-carrying statement — it only carried the strippability risk, which
Rev 1–10 mitigated with a tripwire (decision 5) rather than removing. Rules are therefore ordinary
grammar: `EXPECT <rule> [ON FAILURE <action>]`, attached to the column it protects.

**Steward visibility is preserved; it was never a storage decision.** The original argument for tags
was that stewards see the rules through the tag catalog, lineage, and the Governance dashboard. That
is a *projection*. The read side is fed synthesized `expect`/`fail` entries derived from the AST
clauses instead of from the tag dictionary, and every steward-facing surface shows what it showed
before. Rules-as-visible-metadata plus metrics-as-history (§7) remains the complete steward picture;
only the input encoding changed.

**What the move buys beyond consistency.** The numbered `@expect_N`/`@fail_N` pairing disappears —
clauses simply repeat. The quoted-value mini-language goes with it: `IN ('NA','EMEA')` is written
once rather than doubled inside an outer quote (`'IN (''NA'',''EMEA'')'`). And the rule grammar
becomes real tokens, so diagnostics carry true line/column positions and the formatter, highlighter,
and LSP stop treating rules as opaque text.

## Current state (verified against code)

- **`ASSERT` is boolean-only, hard-fail.** `AssertStatement(Expression Condition, Expression? Message)` (`src/ETL-SQL.Core/Ast.cs:1327`), parsed by `FlowParser.ParseAssert` (`src/ETL-SQL.Core/Parser/Components/FlowParser.cs:279`), dispatched via `_dispatchMap[TokenType.ASSERT]` (`StatementParser.cs:79`), handled by `AssertStatementHandler` (throws `ExecutionException`). No severity, no action clause, no column/job awareness.
- **Trailing-action prior art exists.** `EXPECT SCHEMA … ON DRIFT WARN` (`ExpectSchemaStatement { bool WarnOnDrift }`, `Ast.cs:1343`) parses its trailing clause at `FlowParser.cs:331` by matching `TokenType.ON` then `DRIFT`/`WARN` as **contextual identifiers**. `ON` is a real keyword token; `FAILURE`/`WARN`/`THROW`/`QUARANTINE` can be matched the same way. `ExpectSchemaStatementHandler.cs:125` is the WARN-vs-THROW pattern to reuse. No statement currently parses *multiple stacked* action blocks.
- **The comment-tag pipeline is no longer where rules land (Rev 11).** `/* @tag: val; */` and `-- @tag: val` still lex to a single `TokenType.COLUMN_TAG` (`Lexer.ReadCommentOrTag`, `Lexer.cs:273`), and `Parser.ParseMetadataTags` (`Parser.cs:1865`) still binds descriptive tags positionally to the preceding column (`Parser.cs:1777`) — that pipeline is unchanged and keeps carrying `@d`, `@owner`, `@pii`. What changes is that `expect`/`fail` no longer arrive as `Metadata["expect"]` / `Metadata["fail"]`; they arrive as a typed clause list on `SelectColumn` (`Ast.cs:77`), parsed by the select-list parser (§1–§2).
- **The keyword budget for the new column surface is zero.** `EXPECT` is **already a reserved token** (`Lexer.cs:151` → `TokenType.EXPECT`, used by `EXPECT SCHEMA`), so leading the clause with it adds no reserved word. It is excluded explicitly from the implicit-alias branch of `ParseSelectColumn`, because `IsIdentifier` is deliberately permissive — without that guard `SELECT c EXPECT NOT NULL` would alias the column "EXPECT" and drop the rule. `QUARANTINE` is deliberately **not** a token type (`TokenType.cs:9-11`: *"'quarantine' is the most natural name for a quarantine table"*) — it stays contextual inside `ON FAILURE`, which is the concrete reason the clause keyword leads and the action follows. Rule keywords (`MATCHES`, `CASTABLE`, `EXPR`, `UNIQUE_FIRST`, …) are likewise matched contextually inside the `EXPECT` sub-parser by token **text**, not type — the parser gotcha Slice C already hit with `JOB`/`WITHIN`/`ALERT`/`OF`/`HISTORICAL`.
- **Tag governance + validation seams exist.** `StewardshipTagCatalog.Definitions` (`src/ETL-SQL.Core/Common/StewardshipTagCatalog.cs:40`) is the first-class vocabulary (owner/steward/pii/…); `UnknownTagLintRule` flags anything absent from it. `TagValueValidationRule` validates values; `TagGovernanceRuntimePolicy` (`src/ETL-SQL.Engine/Handlers/TagGovernanceRuntimePolicy.cs`) reads tag values and throws `ExecutionException` at runtime — the closest existing analog to a rule failure action.
- **Severity channels already present.** `ExecutionException` carries an int `Severity` (default 16) + `ErrorNumber`/`State`/`Line` (`src/ETL-SQL.Core/Common/Exceptions/ETLException.cs`); `DiagnosticSeverity { Error, Warning, Info, Hint }` flows into `ExecutionResult.Diagnostics` (`ExecutionResult.cs:16`) — the right surface for a structured non-fatal WARN.
- **A real streaming row pipeline exists.** Query execution is `IAsyncEnumerable<Row>`; the projection step `ProjectRows(...)` in `src/ETL-SQL.Engine/Engines/SelectExecutionEngine.cs:724` (invoked at `:608`/`:706`) is the natural inline validation hook. Write-side precedent: `InMemoryDataSource.WriteBatchesCore` calls `IDataValidator.ValidateCheckConstraint(Expression, Row)` per row (`DataSources.cs`; impl `DataConstraintValidator.cs`).
- **Pushdown runs upstream of the hook.** `SemiJoinPushdownOptimizer` and `PredicatePushdownOptimizer` rewrite the plan before execution (`SelectExecutionEngine.cs:65-69`). They move *filters* toward sources; output rows still flow through `ProjectRows`. Column rules validate **output rows**, so upstream predicate pushdown does not change rule semantics — but any current or future path that bypasses local projection entirely must be **pinned to the local path** when rules are present (§4).
- **Disk-spilling engines exist for the UNIQUE pass.** `ExternalAggregateEngine.ApplyAggregationExternal(groupBy, …, having, …)` (`Engine/Engines/ExternalAggregateEngine.cs:54`) and `ExternalDistinctEngine` (hash-partition; equal keys co-located). Spill thresholds ~100k rows (`JoinSpillThreshold`), 1M for temp tables (`TempTableSpillThresholdRows`), plus a process-wide `MemoryGovernor` ceiling.
- **Temp-table materialization for QUARANTINE exists.** `#name` targets auto-create an `InMemoryDataSource` on first write (`InsertStatementHandler.cs:36`); `INSERT` streams via `WriteBatches(IAsyncEnumerable<DataTable>, append:true)` (`PerformBatchTransfer`). Mid-query temp writes are already proven by the REST connector's `RESPONSE_TABLE=#name` capture.
- **Connectors support sink-only, and outbound HTTP already exists — with egress enforcement.** `IConnector`/`IConnectorRegistry` (`Core/Data/DatabaseConnectors.cs`); most members are default-implemented. Sink-only is idiomatic (`SmtpConnector`/`SmtpDataSource` — `ReadBatches`→`yield break`; Kafka). `RestConnector` (`Connectors/Rest/RestConnector.cs`) does templated outbound POST/PUT with retries and idempotency, and `RestDataSource` (`RestDataSource.cs:34-53`) validates every target host against the egress allowlist (`SecurityService.ValidateHost`), re-validates **every redirect hop** (`:1344`, `:1398`), and disables `UseProxy` so an ambient proxy cannot route around the controls. The webhook connector **must reuse this exact path**. Registration is one `AddSingleton<IConnector,…>` line each in `TUI/App/TuiDependencyInjectionSetup.cs` and `Orchestrator/DependencyInjectionExtensions.cs`, plus `registry.Register(...)` in `LanguageServer/Program.cs`. `CreateConnectionStatementHandler` resolves any registered type — no change needed.
- **Historical run metrics are persisted.** `SQLiteJobHistoryStore` `JobHistory` records per-run `RowsProcessed` (+ `PeakMemoryBytes`, `CpuTimeSeconds`); `GetHistoryAsync(job, limit)` and daily rollup `JobHistoryDailySummary.TotalRows` via `GetJobHistoryDailyAsync` (interface `IJobHistoryStore`, `Core/Data`). Current-run value is `ExecutionResult.RowsProcessed`. **Seam:** this lives in `ETL-SQL.Orchestrator`; statement handlers run in `ETL-SQL.Engine` against `IExecutionContext`, which does not expose it today.

## Design decisions (locked)

1. **Full scope**: column rules **+** `ASSERT JOB` **+** webhook connector.
2. **Rules are steward-facing governance metadata — by projection, not by encoding (Rev 11).** The rule and action attached to a column are published to the tag catalog, lineage, and the stewardship read side **from the AST**, so stewards see which rules protect which columns exactly as they did when the rules were literal tags; per-run DQ metrics (decision 8) complete that picture with observed impact. Nothing on the read side may depend on the rules having been written as comments.
3. **`UNIQUE_FIRST`/`UNIQUE_LAST` require an explicit `BY <key>`** — reject without one. Source/spill/parallel order is not stable, so "first" is otherwise non-deterministic.
4. **`QUARANTINE` is legal only at a sink/materialization boundary** (top-level SELECT, `INSERT … SELECT`, `SELECT … INTO`) — a parse/lint error on nested subquery/CTE columns, because it is a filter with a side effect that would silently change downstream row counts.
5. **Rules are first-class syntax and validation is symmetric (Rev 11).** A malformed or unknown rule or action is a **hard error** (parse + lint), never silently ignored. A column may carry any number of `EXPECT … [ON FAILURE …]` clauses — repetition replaces the numbered `@expect_N`/`@fail_N` pairing, which existed only because a tag dictionary cannot hold repeated keys. Symmetry is retained as a **routing-completeness** check: a column electing `ON FAILURE QUARANTINE` with no matching statement-level `ON FAILURE QUARANTINE TO …` block is a hard error, **and** a statement-level block no column elects is equally a hard error. Note what that check is *no longer* for: in Rev 1–10 it doubled as the comment-stripping tripwire, and the residual it could not cover (`WARN`/`THROW`-only rules with no trailing clause vanishing silently) was a documented limitation. Rules are not in comments any more, so no tool can strip them — the failure mode and its residual are both retired.
6. **Job metrics are collected in-stream during the run, never by post-run re-scan.** A metrics collector wraps the sink-side row stream and computes `ROW_COUNT`, per-column null counts, and quarantine/warn tallies in the same pass. This makes `NULL_PERCENT` near-free, works for **write-only sinks** (Kafka, webhook, SMTP) where a post-run query is impossible, and produces the persisted DQ metrics as a by-product.
7. **The UNIQUE pre-pass is spill-once, single source read.** When any UNIQUE rule is present, the input stream is materialized once to spill storage; both the duplicate-key pre-pass and the main validation pass read from the spill. The source is **never read twice** — a second read is impossible or inconsistent for non-rewindable sources (Kafka, paginated REST), and even for rewindable sources two reads can observe different data.
8. **DQ outcomes are persisted per run.** Rows quarantined, rows warned, and per-rule failure counts are recorded on the run's job-history record and exposed on `ExecutionResult`. Without this there is no trend visibility and `ASSERT JOB` could never assert on quarantine rate — the most natural job-level DQ metric.
9. **The webhook connector inherits REST egress enforcement wholesale.** Arbitrary outbound POST with `SECRET:` access is otherwise an exfiltration primitive. Host validation, per-redirect-hop re-validation, and the proxy-disabled handler are mandatory, and the connector must satisfy `docs/architecture/standards/Connectors_Standards.md` (10 inviolable rules + checklist).
10. **Documentation and LSP support are part of each slice's definition of done**, not a trailing phase. `docs/reference/` is the embedded runtime help (filenames are lookup keywords) — new surface that ships without reference docs is invisible to users at the point of use.
11. **v1 quarantine is replay-ready by construction.** Quarantine captures the **pre-projection input row**, requires an **enclosing section label**, and carries `__dq_status`/`__dq_row_id`/`__dq_run_id`/`__dq_capture_scope` plus a **reserved, always-NULL `__dq_origin_row_id`** from day one — so the v2 remediation workflow (label replay with source substitution, designed below) needs no breaking change to quarantine tables written by v1. Because the target schema is fixed on first write (§ Determinism), the replay-linkage and retention-scope columns must exist before release; adding them later would break v1-created tables.
12. **Quarantine table schema drift is verified, not ignored.** If the target schema of a durable quarantine table does not match the incoming pre-projection schema, the engine will attempt an additive migration (adding columns that are missing) or fail validation safely if data types are incompatible, alerting the steward.
13. **Quarantine and warn targets support configurable data retention.** Both `ON FAILURE QUARANTINE TO` and `ON FAILURE WARN TO` clauses accept a retention configuration (e.g. `WITH (RETENTION = '30 DAYS')`) to allow the engine to prune older records automatically. Retention is especially critical for warn tables, which have no lifecycle state machine to provide natural pruning.
14. **One action vocabulary, one clause spelling (Rev 11).** Every failure action in this feature is written `ON FAILURE <ACTION> [<target>] [WITH (<options>)]`, stackable, drawn from a single vocabulary: `THROW`, `WARN`, `QUARANTINE TO <table>`, `NOTIFY <notification>`. Each surface accepts the subset that is meaningful there and rejects the rest with a specific error (see "Action vocabulary" under Proposed surface). Two consequences for `ASSERT JOB`: `ON FAILURE THROW` is **deleted** — severity is an action, not a clause *name*, and there was never an `ON FAILURE THROW` or an `ON CRITICAL_FAILURE NOTIFY` to make the cross-product coherent — and `WITH (FAIL_ON_WARN = TRUE)` is **deleted**, because it is exactly the existing predicate `WARN_PERCENT = 0` combined with `ON FAILURE THROW`. The flag was also a second, hidden path to failing a run: `AssertJobStatementHandler` threw on `stmt.ThrowOnCritical || failForWarnRows`, so an option buried in a `WITH()` bag overrode the clause whose entire job was declaring severity, and the two supposedly equivalent spellings behaved differently. `WARN` is the default action on **both** surfaces when no clause is written.
15. **"Cannot evaluate" splits into a static error and a runtime skip (Rev 11).** A predicate naming a column — or a target-qualified column — that **no sink statement in the script writes** is a **lint/parse error**. The column-rule side already ruled this way for composite rules (*"a typo would otherwise produce a rule that reports clean because it never ran"*), while `ASSERT JOB` ruled the opposite way for the identical mistake: the handler skipped the predicate with a warning and the assertion passed green, so `NULL_PERCENT(clean_users.Emial) < 0.02` was a guard that could never fire. Skip-with-warning survives only for what is genuinely unknowable until runtime: a run that observed no rows, and `HISTORICAL` cold start below `MinHistoryRuns`.

---

## Proposed surface

### Column rules

```sql
-- Rules are grammar, not comments. A column carries any number of
-- EXPECT <rule> [ON FAILURE <action>] clauses; the trailing statement-level
-- ON FAILURE blocks supply each action's routing target.
-- Comments keep doing what comments do: describing.
-- A quarantining statement must sit inside a section label.
import_users:
SELECT
    UserId   EXPECT NOT NULL ON FAILURE THROW
             EXPECT UNIQUE   ON FAILURE QUARANTINE,
    Email    EXPECT MATCHES '^[^@]+@[^@]+$' ON FAILURE QUARANTINE,
    Age      EXPECT >= 0 AND <= 120,                    -- no action ⇒ WARN
    Region   EXPECT IN ('NA','EMEA','APAC') ON FAILURE QUARANTINE,
    RegionId EXPECT EXISTS IN dim_region(Id) ON FAILURE QUARANTINE,
    EventId  EXPECT UNIQUE_FIRST BY LoadedAt ON FAILURE QUARANTINE,
    LoadedAt /* @d: ingest timestamp, source clock; @owner: platform; */
INTO clean_users
FROM raw_users
ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS')
ON FAILURE WARN TO warning_log_users WITH (RETENTION = '30 DAYS')  -- optional; omit TO for diagnostic-only
ON FAILURE THROW;   -- Up to 3 distinct routing targets are allowed
```

- **Rules** (combinable with `AND`/`OR` — the most-used forms from the dbt/GE/Soda field, per the competitive review): `UNIQUE`, `UNIQUE WITH (<col>, …)` (composite key declared on one column, unique over the tuple), `UNIQUE_FIRST BY <expr>`, `UNIQUE_LAST BY <expr>`, `NOT NULL`, `MATCHES '<regex>'`, `IN (<list>)`, `EXISTS IN <table>(<column>)` (relationship/FK check), `EXPR <predicate>` (cross-column boolean over the full row, e.g. `EXPR StartDate <= EndDate`), and numeric `>= <= > < =`.
- **NULL semantics (defined, not implied)**: `NOT NULL` is the only rule that fails on NULL. Every other rule **skips NULL values** (SQL `CHECK`-constraint convention, matching dbt `accepted_values`) — pair with `NOT NULL` explicitly to reject them. Without this rule, every nullable column would double-fail.
- **Rules evaluate against the projected (post-expression) value.** `SELECT UPPER(Email) EXPECT MATCHES '…'` validates the uppercased value; `__dq_value` records that projected value.
- **`MATCHES` takes a string literal** (`MATCHES '^A.*'`): a bare regex cannot be lexed — `@` would start a variable and the operators would tokenize. Every other rule is written as ordinary tokens — `IN ('NA','EMEA')`, `LENGTH BETWEEN 5 AND 10`, `CASTABLE AS DECIMAL(18,2)` — so the outer quoting and SQL-style quote doubling the tag layer forced (`'IN (''NA'',''EMEA'')'`) are gone.
- **Placement and disambiguation.** The clause follows the column expression and its optional alias: `<expr> [AS <alias>] EXPECT <rule> [ON FAILURE <action>] [EXPECT …]`. `EXPECT` is a reserved token and is excluded from the implicit-alias branch, so `SELECT c EXPECT NOT NULL` needs no `AS`. The **column-level** `ON FAILURE` never takes `TO` or `WITH` — routing is declared once per statement — so a `TO` in column position is a parse error that names the statement-level clause. The statement-level blocks are never ambiguous with the column-level ones because the query body (`INTO`/`FROM`/…) always intervenes.
- **Rules combine with `AND`/`OR`, never with a comma.** In a select list the comma separates columns, and one character cannot mean both; `NOT NULL, UNIQUE` becomes `NOT NULL AND UNIQUE`. A top-level `AND` unrolls into independent rules so each conjunct reports its own failures — the granularity the comma form gave.
- **Actions** (`ON FAILURE <action>` on the column): `THROW` (error, `ExecutionException`), `WARN` (row passes through; aggregated diagnostic always emitted; row optionally captured to a warn table), `QUARANTINE` (row removed from output, written to the statement's `TO` target). Default when a rule is written with no action: **`WARN`** — fail-safe, not silent, and the same default `ASSERT JOB` uses.
- **Repetition replaces numbering.** Several rule/action pairs on one column are written by repeating the clause, as `UserId` does above; the numbered `@expect_N`/`@fail_N` forms are deleted.
- **`ON FAILURE <ACTION> [TO <table>] [WITH (<options>)]`** trailing blocks route each action. Up to three blocks are supported concurrently (`QUARANTINE`, `WARN`, `THROW`). `TO` is **required** for `QUARANTINE` (the row has nowhere else to go) and **optional** for `WARN` (omitting `TO` produces diagnostic-only mode — the aggregated warning fires but no row is written to a table). `THROW` never takes a `TO` target. Symmetric validation (design decision 5) applies: a column electing `QUARANTINE` or `WARN` with no matching statement block, and a statement block no column elects, are both hard errors.
- **Retention Options**: Both `ON FAILURE QUARANTINE TO` and `ON FAILURE WARN TO` targets accept `WITH (RETENTION = '<interval>')` (e.g. `'30 DAYS'`). The engine prunes terminal rows older than the interval within the current job/script `__dq_capture_scope`; active evidence and rows owned by another writer are preserved. Warn tables have no lifecycle pruning beyond retention, so the linter emits a `Diagnostic(Info)` when a `WARN TO` target is declared without a `RETENTION` option, recommending one be set.
- **Quarantine targets should be durable.** `TO` accepts a `#temp` table or a durable table on a named connection. `#temp` evaporates when the run ends — legal for in-script triage, but the linter emits an **Info** diagnostic recommending a durable target, and all documentation examples quarantine to durable tables. "Remediation is the builder's job" only works if the rows survive the run.

### WARN table schema

When `ON FAILURE WARN TO <table>` is declared, each failing-but-passing row is captured to the warn table. The schema is identical to the quarantine table with three differences:

| Column | Quarantine | Warn |
|---|---|---|
| All pre-projection input columns | ✓ | ✓ — same capture, same diagnostic richness |
| `__dq_rule` | ✓ | ✓ |
| `__dq_column` | ✓ | ✓ |
| `__dq_value` | Projected value that failed | Projected value that triggered warn |
| `__dq_reason` | ✓ | ✓ |
| `__dq_ts` | ✓ | ✓ |
| `__dq_run_id` | ✓ | ✓ |
| `__dq_capture_scope` | stable job/script retention owner | stable job/script retention owner |
| `__dq_row_id` | Hash of input row + run_id | ✓ — same hash, same deduplication semantics |
| `__dq_status` | `'quarantined'` (lifecycle: released/replaying/replayed/discarded) | **`'warned'` (fixed — no lifecycle transitions; row is already in the target)** |
| `__dq_origin_row_id` | NULL in v1; v2 replay linkage | **Always NULL — replay concept does not apply to warns** |
| **`__dq_target_written`** | *(absent)* | **`1` (BIT, always) — confirms the row reached the main target despite the rule failure** |

Key behavioral notes:
- **`__dq_status` is immutable for warn rows.** The engine rejects any UPDATE to `__dq_status` on a warn-table row — it is evidence, not a disposition field.
- **No replay manifest is written** for warn records. `REPLAY QUARANTINE` does not apply to warn tables.
- **PII masking applies to the warn table the same as quarantine.** If a source column carries `@pii`, the captured value in `__dq_value` is masked in engine diagnostics and alert payloads; the full value is preserved only inside the warn table itself, which inherits the same stewardship tags and access controls as its source columns.
- **Retention is more critical for warn tables than quarantine**, because warn rows have no lifecycle event that naturally prunes them. The linter nudges toward `WITH (RETENTION = ...)` on every `WARN TO` target.

### Job rules

```sql
ASSERT JOB import_csv (
    ROW_COUNT WITHIN 0.2 OF HISTORICAL,
    NULL_PERCENT(clean_users.Email) < 0.02,
    QUARANTINE_PERCENT < 0.01
)
ON FAILURE NOTIFY data_quality_alerts
ON FAILURE THROW;
```

`ASSERT JOB` takes the **same** stacked `ON FAILURE <action>` blocks as a rule-carrying `SELECT`
(decision 14). `ON CRITICAL_FAILURE` and `WITH (FAIL_ON_WARN = …)` no longer exist.

- **Advisory (the default).** With no `ON FAILURE` block at all, a failed predicate is recorded to
  the log and run diagnostics and the script continues — today's default behaviour, now with an
  explicit spelling, `ON FAILURE WARN`, for authors who want it stated.
- **Tell someone, do not fail the run.** `ON FAILURE NOTIFY <notification>` is **non-fatal on its
  own**. This is the "worth knowing about, not worth stopping for" shape, and it is the answer to
  "how do I warn here": either `ON FAILURE WARN` (record it), `ON FAILURE NOTIFY x` (send it), or
  both stacked. Only `THROW` fails the run, and it must be written.
- **Fail the run.** Add `ON FAILURE THROW`. When stacked with `NOTIFY`, the notification is
  dispatched first regardless of the order written, then the exception is raised — the ordering is
  fixed by the engine, not by the author, because there is only one sensible order.

```sql
-- Advisory: recorded in run diagnostics, exit code unaffected
ASSERT JOB customer_load (WARN_PERCENT < 0.05) ON FAILURE WARN;

-- Notify a channel, still non-fatal — calibrating a new rule
ASSERT JOB customer_load (WARN_PERCENT < 0.05) ON FAILURE NOTIFY data_quality_alerts;

-- Any warned row fails the run: the replacement for WITH (FAIL_ON_WARN = TRUE)
ASSERT JOB customer_ci (WARN_PERCENT = 0) ON FAILURE THROW;
```

The `FAIL_ON_WARN` replacement is exact, including the empty-run edge: with zero validated rows the
warn ratio is unobservable and the predicate skips (decision 15), while the old flag saw
`RowsWarned = 0` and also passed. Both spellings agree, and now there is only one of them.

### Action vocabulary

One vocabulary; each surface accepts the subset that is meaningful there and rejects the rest with a
specific error naming the surface that does accept it.

| Action | Column `EXPECT` | Statement `ON FAILURE` (SELECT) | `ASSERT JOB` |
| :--- | :--- | :--- | :--- |
| `WARN` | ✓ — default when no action is written | ✓ — optional `TO <table>`, `WITH (RETENTION = …)` | ✓ — default when no block is written |
| `THROW` | ✓ | ✓ — never takes `TO` | ✓ |
| `QUARANTINE` | ✓ | ✓ — `TO <table>` **required** | ✗ — a job metric has no row to divert |
| `NOTIFY <notification>` | ✗ | ✗ | ✓ |

Two deliberate gaps, recorded so they are not read as oversights:

- **`NOTIFY` is not accepted at statement level.** A per-statement notification would fire once per
  materializing statement per run, which is an alert-storm generator; `ASSERT JOB` is the job-level
  surface where a run summarises itself once. If demand appears, the vocabulary has room for it
  without a grammar change — that is the point of having one vocabulary.
- **Per-predicate severity is not modelled.** `ON CRITICAL_FAILURE` may have been reaching for
  "some predicates are advisory, some are fatal", but it never expressed that: it was a
  statement-level toggle. If per-predicate severity is wanted later it belongs **on the predicate**,
  not on the clause name.

### Notification destination

```sql
CREATE CONNECTION alerts_webhook AS WEBHOOK(URL = 'SECRET:slack_url', FORMAT = 'slack');
CREATE NOTIFICATION data_quality_alerts USING alerts_webhook;
```

The webhook remains a general-purpose sink: any script can `INSERT INTO` it. `ASSERT JOB` routes
through a named Orchestrator notification so jobs, data-quality assertions, and report alerts share
one destination catalog.

---

## Component design

### 1. Rule model + mini-DSL parser — new `src/ETL-SQL.Core/Quality/`

- `ColumnRule.cs`: abstract `ColumnRule` + `NotNullRule`, `UniqueRule(UniqueMode Mode, Expression? OrderKey, IReadOnlyList<string>? CompositeColumns)`, `MatchesRule(string Pattern)`, `ComparisonRule(CompareOp Op, decimal Value)`, `InListRule(IReadOnlyList<object?> Values)`, `ExistsInRule(string Table, string KeyColumn)`, `ExprRule(Expression Predicate)`. Enums `UniqueMode { All, First, Last }`, `FailAction { Throw, Warn, Quarantine }`. `MATCHES` patterns compile with **`RegexOptions.NonBacktracking`** — a per-row user-supplied regex is otherwise a ReDoS vector that can hang the engine mid-pipeline; the linter rejects constructs NonBacktracking cannot compile (backreferences, lookaround).
- `ColumnExpectClauseParser.cs`: parses `EXPECT` clauses **from the shared token stream** (Rev 11) as a select-list sub-parser. The bespoke mini-tokenizer the string form needed and its outer-quote stripping both disappear: the lexer already produced the tokens, bounds and predicates go through the engine's own expression parser, and every rule carries a real source position. Rule keywords are matched contextually by token text (see Current state).
- `ColumnRuleParser.cs` survives as the **read-side** parser for the `expect`/`fail` tags projected onto lineage (§3) — the catalog, Portal, and `SHOW DATA QUALITY RULES` all re-read rules from there. It is never the authoring path.

### 2. Trailing `ON FAILURE` clause — Core parser + AST

- Extend `SelectStatement` with `IReadOnlyList<FailureActionClause>? OnFailureActions`; add `FailureActionClause(FailAction Action, string? Target, RetentionInterval? Retention)`.
- **Column-level clauses (Rev 11).** `SelectColumn` gains `IReadOnlyList<ColumnExpectClause>? Expectations`, where `ColumnExpectClause(IReadOnlyList<ColumnRule> Rules, FailAction Action, bool ActionExplicit, string Text)` — `Action` defaults to `Warn` when the author writes none, and `Text` is sliced from the source so a rule reports itself as written. A `TO` or `WITH` following a column-level action is a `SyntaxException` pointing at the statement-level clause.
- **`AstSerializer` must round-trip the new form.** The serializer is the formatter's output path and is covered by round-trip property tests; rules that survive a parse but not a re-serialize would silently drop enforcement on any tool-formatted script.
- Parse after the query body, before `;`, mirroring `ParseExpectSchema`'s `ON DRIFT WARN` (`FlowParser.cs:331`). `QUARANTINE` **requires** `TO <table>`; `WARN` **optionally** takes `TO <table>` (no `TO` = diagnostic-only mode, no row capture); `THROW` never takes `TO`.

### 3. Steward projection + validators — Analysis

- **Projection, not registration (Rev 11).** `expect`/`fail` are no longer tag-catalog *inputs*. `ColumnExpectProjection` publishes them onto the lineage record from `SelectColumn.Expectations`, so the tag catalog, lineage, Portal, and `SHOW DATA QUALITY RULES` render them exactly as before with no read-side change. They stay registered in `StewardshipTagCatalog` so those surfaces resolve a definition for what they read back. Only a **written** action is projected: a defaulted `WARN` is left absent so the read side can still distinguish a deliberate WARN from one nobody chose.
- `ColumnRuleValidationRule.cs` changes job: the rule grammar is now the parser's, so what the linter catches is a rule that never became grammar at all — `/* @expect: … */` still lexes as an ordinary comment, so it is reported as an **Error** naming the `EXPECT` clause to write instead.
- `JobMetricColumnRule.cs` (new): an `ASSERT JOB` predicate naming a column no sink in the script writes is an **Error** (decision 15). A sink whose columns cannot be enumerated (`SELECT *`) makes every name plausible and is left alone.
- `QuarantineBoundaryRule.cs`: `Diagnostic(Error)` when a column elects `QUARANTINE` on a non-sink SELECT, when a `QUARANTINE` action lacks a `TO` target, when a quarantining statement has **no enclosing section label** (`SectionLabelStatement`, `Ast.cs:1549` — the label is the v2 replay re-entry point, required from v1), **and — symmetric check — when any statement-level `ON FAILURE <ACTION>` block is elected by no column in the statement** (routing completeness; Rev 11 retired its second job as the comment-stripping tripwire). `Diagnostic(Info)` when quarantining to a `#temp` target (recommend durable); `Diagnostic(Info)` when a `WARN TO` target is declared without a `RETENTION` option (warn tables have no lifecycle pruning). The linter may also warn when a `UNIQUE_FIRST/LAST` `BY` key isn't provably unique.

### 4. Runtime enforcement — Engine

- `src/ETL-SQL.Engine/Services/ColumnQualityValidator.cs` (model on `DataConstraintValidator.cs` / `TagGovernanceRuntimePolicy.cs`), invoked by wrapping the `ProjectRows(...)` stream (`SelectExecutionEngine.cs:724`) in a validating async iterator. **Zero rules ⇒ zero overhead** (the path is skipped).
  - Passing synchronous rules use a synchronous `ValueTask` fast path with indexed rule traversal;
    they do not allocate an async state machine or boxed interface enumerator per row. `EXPR`
    predicates and actual quarantine/warn target writes remain asynchronous. The allocation budget
    is pinned at no more than 4 KB of measurement noise over 100,000 passing rows (down from 43.2 MB).
  - **Per-row rules** (NotNull/Matches/Comparison/In/Expr) evaluate inline against the projected value; `EXPR` predicates get the full projected row. NULL values **skip** every rule except `NOT NULL` (see Proposed surface). Honor `SET CASE_SENSITIVE` for MATCHES/IN/EXISTS IN. Numeric compares are **decimal** at runtime. THROW → `ExecutionException`; WARN → aggregated (below); QUARANTINE → divert row.
  - **EXISTS IN** builds its key set once per statement from the referenced table (hash set via the existing spill-aware infrastructure; reference tables are typically dimension-sized), then probes per row. The build honors `SET CASE_SENSITIVE`.
  - **WARN is aggregated, never per-row.** Per-row diagnostics on a 10M-row load with a high failure rate is a diagnostics DoS. The validator keeps, per (rule, column): a failure **count** plus the first **N sample values** (default 10, configurable under `appsettings.json → Engine`), and emits **one** `Diagnostic(Warning)` per (rule, column) at end of stream with count + samples. Per-row detail goes to Debug-level logging only.
  - **PII masking in samples and notifications.** Sample values from a `@pii`-tagged column are **masked** in warn diagnostics, logs, and every notification payload (`ASSERT JOB … NOTIFY` summaries) — counts stay, values don't. A governance feature must not exfiltrate PII to Slack. The full value is preserved only inside the quarantine table itself, which carries propagated stewardship tags and access controls (see Determinism & edge cases).
  - **UNIQUE rules run over a single spill materialization** (design decision 7). The validating iterator spills the upstream stream once (respecting `JoinSpillThreshold`-class thresholds and the `MemoryGovernor`); the duplicate-key set is built from the spill via `ExternalAggregateEngine.ApplyAggregationExternal(groupBy=[col], HAVING COUNT(*)>1)` (composite `UNIQUE WITH` groups by the column tuple — same engine, multi-column key) — for `UNIQUE_FIRST/LAST BY key` also aggregating `MIN/MAX(orderKey)` per group so only the keeper survives — then the main pass streams from the same spill. Cost is one extra disk write/read of the stream, documented. One pre-pass per unique column in v1 (single-pass batching is a noted optimization).
  - **Rules pin execution to the local path.** Upstream predicate/semi-join pushdown is unaffected (it moves filters, and rules validate output rows), but any plan shape that would bypass local projection entirely is disabled for statements carrying `EXPECT` rules, with a regression test guarding the pin.
- **QUARANTINE routing**: resolve the `TO` target via `context.ResolveDataSourceAsync` (auto-create for `#temp`), write with `WriteBatches(append:true)`. **The captured row is the pre-projection input row** — every input column the statement saw, available directly in the `ProjectRows` wrapper — not the projected output row. This is what makes v2 replay possible (re-feed the row through the statement) and it is also better for stewards: they fix the *cause* (the source value), not the symptom. Rows are **augmented** with `__dq_rule`, `__dq_column`, `__dq_value` (the projected value that failed), `__dq_reason`, `__dq_ts`, `__dq_run_id`, `__dq_capture_scope`, `__dq_status` (always `'quarantined'` when written — the v2 disposition column, shipped in v1 so remediation never breaks the schema), `__dq_row_id` — a deterministic hash of the captured row content + run id, the stable identity replay-once semantics key on — and a **reserved `__dq_origin_row_id`** written as NULL in v1. The latter is the forward-compat hook for decision 11: v2 replay populates it when an edited-but-still-failing row re-quarantines (linking the new row back to the original `__dq_row_id`), and because the quarantine schema is frozen on first write (§ Determinism), the column must be present in v1-created tables or v2 could not write to them. The engine routes and annotates; the **remediation workflow ships as v2** (designed below) — v1 users remediate by hand against the same schema.
- **WARN routing**: two modes depending on whether `TO` is present:
  - **Diagnostic-only** (`ON FAILURE WARN` with no `TO`): the aggregated end-of-stream `Diagnostic(Warning)` fires (count + N capped samples); no row is written anywhere. This is the lightest mode — no storage overhead, message visible in the run log and LSP output.
  - **Row-capture** (`ON FAILURE WARN TO <table>`): in addition to the aggregated diagnostic, each individually failing row is written to the warn table in the same `WriteBatches(append:true)` pattern as quarantine. The captured row is the **pre-projection input row** augmented with the same `__dq_*` columns as quarantine, except `__dq_status` is always `'warned'` (immutable), `__dq_origin_row_id` is always NULL (no replay), and `__dq_target_written` is always `1` (confirms the row reached the main target). Retention pruning fires at the end of the run when a `RETENTION` interval is configured. No replay manifest is written for warn records.

### 5. Webhook connector — new `src/ETL-SQL.Connectors.Messaging/Webhook/`

- `WebhookConnector : IConnector` (Name `"WEBHOOK"`, aliases `"SLACK"`, `"TEAMS"`) + `WebhookDataSource : IDataSource` (sink: `ReadBatches`→`yield break`, `WriteBatches`→POST JSON). Model on `RestConnector` + `SmtpDataSource`.
- Options: `URL` (accepts `SECRET:`/`${env}`), `FORMAT = slack|teams|generic` (shapes payload), optional `BODY_TEMPLATE`, timeout/retries. Reuse `RestConnector`'s HTTP/retry path.
- **Security (mandatory, not optional):** every request routes through the same egress enforcement as `RestDataSource` — `SecurityService.ValidateHost` on the target, re-validation of **every redirect hop**, and the `UseProxy = false` handler so an ambient proxy cannot bypass policy. `SECRET:`-resolved URLs are redacted in logs, errors, and diagnostics. The connector must pass the `Connectors_Standards.md` inviolable rules + 25-item checklist before merge.
- Register in the two DI setups + LSP. Uses built-in `HttpClient` — **no new third-party dependency** (no `THIRD-PARTY-INVENTORY.md` change needed).

### 6. In-stream job metrics collector + persisted DQ outcomes — Engine + Orchestrator

- `src/ETL-SQL.Engine/Services/JobMetricsCollector.cs`: a lightweight accumulator wrapping the sink-side stream of materializing statements. Always cheap: row count and quarantine/warn tallies fall out of the `ColumnQualityValidator` pass. **Per-column null counts are collected only for columns named in the script's `ASSERT JOB` predicates** — the Evaluator holds the full `Script` AST, so a pre-walk registers required columns with the collector before execution (zero predicates ⇒ zero per-cell overhead).
- Column resolution for `NULL_PERCENT(col)`: unqualified predicates resolve the column across the run's sink writes; if multiple sink statements write a column of that name, the assert fails with a clean ambiguity error. The v2 metric-depth slice added qualified `NULL_PERCENT(target.col)` and target-aware historical baselines. Because metrics come from the stream, **write-only sinks (Kafka, webhook, SMTP) are fully supported** — no post-run query against the target ever occurs.
- **Persistence**: extend the job-history record with `RowsQuarantined`, `RowsWarned`, and a compact per-rule failure-count payload; surface the same values on `ExecutionResult`. Store changes are **additive** (SQLite + PostgreSQL providers; rolling-expand safe). This is what gives stewards trend visibility and feeds the Governance read side later.

### 7. `ASSERT JOB` + HISTORICAL — Core parser, Engine handler, Orchestrator seam

- In `FlowParser.ParseAssert`, peek for a contextual `JOB` token → `ParseAssertJob`. AST (Rev 11): `AssertJobStatement(string JobName, IReadOnlyList<JobMetricPredicate> Predicates, IReadOnlyList<FailureActionClause>? OnFailureActions)` — the **same** `FailureActionClause` the SELECT clause uses, with `NOTIFY`'s notification name in `Target`. `FailureNotification` and `ThrowOnFailure` become derived properties; `FailOnWarn` and its `WITH (FAIL_ON_WARN = …)` parsing are deleted, and writing that option is a syntax error naming its replacement. An empty list means `WARN`. `QUARANTINE` here is a parse error naming the SELECT clause.
- v1 predicates: `ROW_COUNT WITHIN <frac> OF HISTORICAL`, `NULL_PERCENT(<col>) <op> <v>`, `QUARANTINE_PERCENT <op> <v>`, and simple recorded-metric compares. All current-run values come from the `JobMetricsCollector` (§6) — never a re-scan.
- **HISTORICAL** = mean of the last N completed runs' recorded metric (N configurable, default 5) via `IJobHistoryStore.GetHistoryAsync`; `WITHIN f` ⇒ `|cur − base| / base ≤ f`.
- **Cold start is defined, not accidental**: `HISTORICAL` requires a minimum of `MinHistoryRuns` completed runs (default **3**, configurable). Below the minimum, the predicate is **skipped with a `Diagnostic(Warning)`** ("insufficient history: n of 3 runs") — the job's first deployments must not alert-storm. Non-`HISTORICAL` predicates always evaluate.
- **Seasonality is a known v2**: mean-of-last-N will false-positive on weekly load patterns (Monday ≠ Sunday). `JobHistoryDailySummary` / `GetJobHistoryDailyAsync` already exist, so a same-weekday baseline is a cheap follow-on — deliberately out of v1 scope, recorded below so it isn't forgotten.
- **Engine→Orchestrator seam**: new narrow `src/ETL-SQL.Core/Data/IJobMetricsProvider.cs`, implemented in Orchestrator over `IJobHistoryStore`, exposed on `IExecutionContext`. Null in pure-engine/CLI contexts ⇒ `HISTORICAL` predicates fail cleanly ("requires orchestrator history"); collector-backed predicates (`NULL_PERCENT`, `QUARANTINE_PERCENT`, plain `ROW_COUNT` compares) still work everywhere.
- Handler `AssertJobStatementHandler.cs`: on any predicate failure, walk the declared actions once — `WARN` records to log + run diagnostics; `NOTIFY` resolves the named Orchestrator notification and POSTs a summary through its configured connection, with `@pii`-tagged column values masked (metric values and counts only, never sample data from PII columns); `THROW` raises **after** any notification has been dispatched. The old `if (stmt.ThrowOnCritical || failForWarnRows) throw` becomes one branch driven by the presence of a `THROW` action, so there is exactly one place that decides whether the run fails. Delivery failure keeps its own policy (log + continue by default), independent of whether `THROW` was declared.
- **Unobservable vs. unknown (decision 15).** The runtime skip narrows to "this run observed no rows" and `HISTORICAL` cold start; a predicate naming a column no sink in the script writes is rejected by `JobMetricColumnRule` at author time, with a position.

---

## v2 — Quarantine remediation (designed; not v1 scope)

Script-first with a Portal front end over the same mechanism. v1 ships the hooks (design
decision 11); v2 ships the workflow.

### Disposition model

`__dq_status` flows `quarantined → released → replaying → replayed`, with `discarded` available
before replay. Stewards **edit rows with
plain SQL** — no new edit syntax:

```sql
UPDATE quarantine_users
SET Email = 'karen.chen@acme.com', __dq_status = 'released'
WHERE __dq_row_id = 'a41f…';
```

The Portal steward UI is a grid over the same table issuing the same UPDATEs plus audit events —
one mechanism, two front ends, so the paths cannot drift. The engine rejects edits to `__dq_*`
evidence columns other than `__dq_status`: the failure record is not editable.

**As built so far:** updates that touch `__dq_*` columns are pinned to the engine-side update path
so the lifecycle is enforced before mutation. `quarantined` rows may move to `released` or
`discarded`; replay claims `released` rows as `replaying`; `replaying` rows may return to
`released` after a verified-safe retry decision or move to `replayed` after target-side
verification. `replayed` and `discarded` are terminal except idempotent self-updates. Rows with
status `warned` cannot change status.

### Replay = resume-at-label + source substitution

At quarantine time the engine writes a **manifest** to the **orchestrator state store**:
*(job, script, section label, substituted source table, quarantine target, replayable flag,
input-schema fingerprint)*. The source binding is recorded explicitly at quarantine time — never
inferred positionally ("first table in the section") at replay time, which would break silently
when someone adds an earlier table read to the section.

**As built so far:** orchestrator-hosted runs persist that manifest through the
`IJobMetricsProvider`/job-state seam on first quarantine write. The stored payload includes the
captured input column list as well as the fingerprint, and records a non-replayable reason for
unsupported shapes such as joins. The replay statement now consumes that manifest and takes a
cluster lock through the orchestrator metrics seam before scanning released rows.

`REPLAY QUARANTINE <quarantine_table>;` (script statement; the Portal **Replay** button enqueues
the same as an orchestrator run) resolves the manifest and re-runs the job via the existing
resume machinery (`Evaluator.ResumeLabel`, `Evaluator.cs:1009`) with one substitution: the
recorded source table is fed from rows claimed as `__dq_status = 'replaying'` with the
`__dq_*` columns stripped. Because released rows re-enter the **current statement**:

**As built so far:** the statement resolves the manifest, fails clearly when the manifest is missing
or marked non-replayable, builds an in-memory source stream from released rows with `__dq_*`
evidence columns stripped, and resumes the recorded section label through the existing evaluator
resume path. Before target-side work it claims the released set as `replaying`; after a
successful replay it flips that set to `replayed`. A failed run leaves the claim unresolved so a
later replay cannot duplicate target writes without an explicit steward recovery decision. It
takes the replay lease before claiming rows and releases it when replay finishes or fails.

- **current rules re-apply naturally** — no rule snapshot, no drift; if rules changed and a row
  still fails, it lands back in quarantine, which is the correct outcome;
- a still-failing row re-quarantines as a **new** row linked via `__dq_origin_row_id` (the
  steward's edit changed the content, so the content hash — the row's identity — changed);
- everything after the label re-runs, so in-script downstream statements propagate the fix;
  cross-job propagation can chain an orchestrator job trigger after a successful replay.

Replay takes a **lease on the quarantine target** through the orchestrator's existing
lease/fencing machinery before reading released rows — two stewards clicking Replay concurrently
must not both consume the same `released` set and double-insert.

`released → replayed` flips only after the section completes successfully; a mid-section throw
leaves rows `released` so retry is safe. Edge case: a steward edit can make a row fail the
statement's `WHERE` clause — it is consumed and marked `replayed` but validly filters out and
never reaches the target (documented).

Section re-runnability (rebuild/merge semantics, not blind append) is the **existing label
contract** — labels exist for resume-after-error — restated in the DQ docs, not invented here.

### v2.0 restriction: single-table inputs

Replay substitutes rows at the *source* position, so it is only sound when the quarantining
statement's input pipeline is a single table scan — there the captured pre-projection row **is**
a source row. Quarantine on join statements still works, but the manifest marks it
**non-replayable** and `REPLAY` fails with a clear message ("quarantine source spans a join;
replay requires a single-table input in this version"). Join replay needs row provenance — see
**v3 direction** below.

**Replayability lint (ships with v2):** most rules users place on join statements reference only
base-table columns. When every rule on a join statement references columns from a single input
table, the linter emits a `Diagnostic(Hint)`:

> *"These rules reference only columns from `raw_users`; validating in a single-table statement
> before the join makes quarantined rows replayable."*

This steers scripts toward the shape v2 already fully handles — `label: → validate/quarantine the
raw table → join clean rows` — which is also better pipeline design (rows fail before paying for
the join). It shrinks the population of non-replayable quarantines before v3 exists.

### v2.0 restriction: entry-script quarantines

Scripts chain via `RUN SCRIPT` to arbitrary depth, and quarantine works at **any** depth — but
**replay requires the quarantining label to be in the job's entry script** (the file the job's
`RUN SCRIPT` body runs; a job body is already required to be exactly one `RUN SCRIPT` statement,
`CreateJobStatementHandler.cs:55`, so depth 1 is the normal case and is unaffected). The existing
resume machinery validates labels against the current script's own statements (`Evaluator.cs:1011`)
— cross-script resume does not exist, and v2 does not build it.

The engine knows the `RUN SCRIPT` call stack at quarantine time, so the manifest records the
**full chain and depth**; a nested quarantine gets the non-replayable flag and `REPLAY` fails
with the precise chain: *"quarantined at `master.etlsql → load.etlsql → import_users.etlsql`;
replay requires the label in the entry script in this version."* Same pattern as the join
restriction: quarantine always works, replay names exactly why it can't. Lifting the restriction
is a recorded future direction (below). The replayability lint extends here too: quarantining
sections belong in the entry script, or in children designed to run independently with all inputs
passed as parameters — the modularity discipline the parameter system already encourages.

### Governance

Release / replay / discard are audited through the governance outbox. Quarantine tables carry
propagated stewardship tags (v1) and the Portal surface applies the same RLS/PII controls as any
data view. Replay outcomes (rows replayed / re-quarantined / discarded) land in the per-run DQ
metrics, so stewards see **resolution rate**, not just failure rate. Manifest residency in the
orchestrator store means **replay requires an orchestrator**; CLI-only deployments still
quarantine (v1 behavior) but remediate by hand.

---

## v2 — metric depth (designed; not built)

The v1 predicate set covers volume, quarantine rate, and warn rate. Three gaps were documented as
limitations rather than papered over; this section designs them. They share one piece of new
storage, so they are built together.

### Per-column run metrics — the storage that unblocks the rest

**Problem.** `NULL_PERCENT(col) WITHIN f OF HISTORICAL` parses today and is then skipped with a
warning, because only *aggregate* per-run counts are persisted — there is no per-column series to
average. The same missing storage is why `NULL_PERCENT(target.col)` cannot be qualified: with one
number per run there is nowhere to record which sink it came from.

**Design.** A narrow child table of the run record:

```text
JobColumnMetrics(
    JobHistoryId  INTEGER NOT NULL,   -- FK to JobHistory.Id; prunes with its parent
    TargetTable   TEXT     NULL,      -- the sink the column was written to (NULL = unqualified v1 rows)
    ColumnName    TEXT NOT NULL,
    TotalRows     INTEGER NOT NULL,
    NullRows      INTEGER NOT NULL,
    PRIMARY KEY (JobHistoryId, TargetTable, ColumnName)
)
```

- **Written only for registered columns.** The existing pre-walk registers exactly the columns an
  `ASSERT JOB` predicate names, so a job with no `NULL_PERCENT` predicate writes no rows and pays
  nothing — the v1 zero-overhead property is preserved unchanged.
- **Rows are tiny and bounded**: one row per tracked column per sink per run, typically one to
  three.
- **Additive and rolling-expand safe**, created through the same `EnsureInitializedAsync` path as
  the existing tables, so it lands on both the SQLite and PostgreSQL providers. When the table is
  absent (a store mid-rollout), the provider returns no history and the predicate behaves exactly
  as it does today: skipped with a warning. No behavior regression during a rolling upgrade.
- **Pruning is free** — the rows are keyed by `JobHistoryId`, so existing history pruning removes
  them with their parent. The daily roll-up deliberately does **not** aggregate them; a mean of
  means across days is not a number anyone should assert on.

**Provider extension.** `IJobMetricsProvider` gains one method, mirroring the existing one:

```csharp
Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
    string jobName, string? targetTable, string columnName, int limit, CancellationToken ct = default);
```

Baseline is the mean of per-run null *fractions* (`NullRows / TotalRows`) over the last N completed
runs — the mean of the ratios, not the ratio of the sums, so one enormous run cannot dominate the
baseline. Runs with `TotalRows = 0` are excluded. The existing `MinHistoryRuns` cold-start rule and
zero-baseline skip apply unchanged.

**Qualified `NULL_PERCENT(target.col)`** then falls out of the same storage: the parser already
has to accept a dotted name, `TargetTable` is the discriminator, and the v1 multi-sink ambiguity
error narrows to "ambiguous *and* unqualified". Unqualified predicates keep working when only one
sink writes the column, so no existing script changes.

### `FRESHNESS(<column>) < <interval>`

Data-recency checks are table stakes across dbt, Soda, and Monte Carlo, and this is a small
predicate on infrastructure that already exists. The collector tracks the **maximum value** of a
registered timestamp column as rows stream past — the same registration path as null counts, so
again zero cost when unused. The predicate compares `now − max(col)` against a
`RetentionInterval`-style literal, reusing the interval parser shipped in v1:

```sql
ASSERT JOB import_events (FRESHNESS(EventTime) < '2 HOURS') ON FAILURE THROW;
```

This catches the failure mode volume checks miss entirely: a feed that delivers its usual row count
every night but has silently stopped advancing.

### `WITHIN <n> SIGMA OF HISTORICAL`

A relative tolerance has to be hand-tuned per job, and a job whose volume legitimately varies gets
either false positives or a tolerance so wide it never fires. A standard-deviation band self-tunes:

```sql
ASSERT JOB import_csv (ROW_COUNT WITHIN 3 SIGMA OF HISTORICAL);
```

Mean and population standard deviation over the same last-N window the mean baseline already
loads — no new storage, no new provider call, only a second aggregate over data already in hand.
Cold start needs a **higher** minimum than the mean baseline (a stddev over three points is
meaningless); `MinSigmaHistoryRuns` defaults to 10 and skips with the same warning below it. A
zero standard deviation (a perfectly stable job) would collapse the band to equality — failing the
run on a single extra row, exactly the alert-storm behavior sigma exists to prevent — so it falls
back with a warning to a relative tolerance around the mean of **1% per requested sigma**
(`WITHIN 3 SIGMA` ⇒ 3%). When the baseline is also zero there is no band to define and the
predicate is skipped.

### Seasonality — same-weekday baselines

Mean-of-last-N false-positives on weekly patterns: a Monday run compared against a window
containing weekend runs drifts for entirely healthy reasons. `JobHistoryDailySummary` and
`GetJobHistoryDailyAsync` already exist, so a `WITHIN f OF HISTORICAL BY WEEKDAY` variant is a
filter on the history query rather than new machinery. Deferred behind the three items above
because it only matters once a job has months of history.

---

## v2 — alert quality (shipped)

Once `ASSERT JOB … NOTIFY` is in real use, the failure mode is **notification fatigue**: a job that fails
its assertion every night posts to Slack every night, and the channel gets muted — at which point
the alerting is worse than none, because it is trusted and silent.

**Transition-based alerting.** Alert on pass→fail and fail→pass *transitions* rather than on every
failing run. The shipped implementation stores one JSON alert-state value in `JobState` per
job+assertion signature through the `IJobMetricsProvider` seam.

- **pass → fail**: alert, as today.
- **fail → fail**: suppress, unless a configurable re-alert interval has elapsed (default: once per
  24h, so a persistent problem resurfaces without spamming).
- **fail → pass**: send a **recovery** notification. This is the half that makes an alerting
  channel trustworthy — an alert with no all-clear trains people to ignore the channel.

Suppression is visible: a suppressed alert still logs and still lands in the run's
diagnostics. Silence in Slack must never mean silence in the run record.

---

## v2 — scale and operational hardening (demand-triggered; not scheduled)

These are known ceilings, not defects. Build them when a real workload hits one; each has a
recorded trigger so the decision is evidence-based rather than speculative.

| Item | Trigger | Approach |
| :--- | :--- | :--- |
| Spill-aware UNIQUE key map | Shipped in v0.17.0 scale hardening | Projected key records spill into hash partitions and reduce partition-by-partition; only duplicated groups remain in the validation lookup. |
| Single-pass UNIQUE batching | Shipped in v0.17.0 scale hardening | One pre-pass collects all unique keys for all UNIQUE rule occurrences simultaneously instead of one pass per column. |
| Connector-side retention | Operators ask why `WITH (RETENTION = …)` does not prune a durable quarantine table outside SQLite | Targets can opt in through `IDataQualityRetentionPruner`; SQLite issues a bounded connector-side delete on `__dq_ts`. Additional durable connectors remain demand-triggered. |
| Reusable quarantine-preview session | **Measured and declined for v0.18.0** — preview-session startup is ~0.8 ms. Revisit if it exceeds a 250 ms median or 500 ms p95. See below. | A bounded read-only session reused across previews, which would have to re-establish parsing, linting, policy, RLS, timeout, row-cap and redaction guarantees that a single-shot session gets for free. |

### Quarantine preview session startup — measured, not estimated

`GET /api/data-quality/quarantine/rows` builds a fresh `ExecutionSession` per request. The open
question was whether that per-request cost is small enough for the steward queue to poll the preview
or refresh a dashboard from it, or whether a bounded reusable session has to come first.

Measured by `QuarantinePreviewStartupMeasurement` (Portal DI, 5 warm-up then 25 timed iterations,
three consecutive runs on one machine):

| Run | min | median | p95 | max |
| :--- | ---: | ---: | ---: | ---: |
| 1 | 0.7 ms | 0.7 ms | 1.2 ms | 13.5 ms |
| 2 | 0.7 ms | 0.8 ms | 1.2 ms | 14.5 ms |
| 3 | 0.8 ms | 0.8 ms | 1.1 ms | 13.5 ms |

**Scope of the number, stated so it is not over-read.** This is session construct → execute a
trivial statement → dispose. It deliberately excludes the quarantine target's own connector read,
because that is what a preview mostly costs and it is *not* what a reusable session would change.
The `max` column is the first timed iteration each run — JIT and first-allocation tail, not steady
state.

**Threshold.** Revisit when preview-session startup exceeds a **250 ms median or 500 ms p95** —
the point at which per-poll overhead becomes a visible fraction of a one-second poll interval and a
reusable session starts to earn the correctness risk it carries.

**Decision: do not build the reusable preview path.** The measurement is roughly 300× under that
threshold, so the optimisation would buy about a millisecond per request while requiring every one
of the parsing, linting, policy, RLS, timeout, row-cap and redaction guarantees to be re-established
across a shared session. That is a large correctness surface bought with a negligible gain, and the
guarantees are the whole reason the preview is allowed to read raw quarantined rows at all.

**Polling and dashboard refresh are therefore not blocked by session cost.** If either turns out to
be too slow, the cause will be the target read or the row cap, and that is where to look — not here.

---

## v2 — Governance dashboard integration (follow-on)

The persisted per-run DQ metrics were designed as this feature's feed. The dashboard reads the
lineage/stewardship side already; surfacing DQ findings there means a steward sees *which rules
protect a column*, *how often they fire*, and — once quarantine remediation ships — *how quickly
failures get resolved*. Sequenced last because it consumes the other slices' output.

---

## v2 sequencing

Recommended order, with the reasoning rather than just the list:

1. **Metric depth** (NULL_PERCENT historical, qualified NULL_PERCENT, FRESHNESS, SIGMA). Shipped;
   closes the original documentation gaps and makes alerting worth improving. One piece of new
   storage unblocks all four.
2. **Alert quality** (transition alerting, recovery notifications). Cheap, and it protects the
   credibility of the alerting channel before slice 3 makes alerts more numerous.
3. **Quarantine remediation** (designed above). The headline v2 promise and by far the largest
   slice: disposition model, `REPLAY QUARANTINE`, orchestrator manifest, replay lease, Portal
   steward grid. **Jumps to first if user feedback shows quarantine tables accumulating** — v1
   ships capture with no workflow, so hand-remediation is the known cost of this ordering, and
   evidence of that cost outranks this recommendation.
4. **Scale hardening** — demand-triggered per the table above, not scheduled.
5. **Governance dashboard integration** — consumes the output of 1–3.

Seasonality sits inside slice 1's design but is deferred within it: it only pays off once jobs have
months of history, which no deployment will have at v2 time.

---

## v3 — join-statement replay via probe-side provenance

The v3 objective is to lift v2's single-table replay restriction for the common star-schema
enrichment case: a streamed fact/probe row joins to at most one dimension/build row, then a
data-quality rule quarantines the joined output. The replay contract remains the same as v2:
released rows re-enter the real statement under current rules, protected by the same replay lease
and disposition lifecycle.

### V3 manifest contract

`QuarantineReplayManifest` keeps the v2 fields and adds backward-compatible provenance fields:

- **ReplayMode** — `single-table` for v2 behavior, `probe-join` for v3 join replay.
- **ProbeSourceTable** — the source table whose row will be substituted during replay.
- **JoinBuildTable** — the build-side table used to establish the replayability gate.
- **JoinObservedN1** — true only when the run observed at most one build row per probe key.
- **JoinNonReplayableReason** — precise join-specific reason when probe replay is blocked.

These fields are surfaced through the Portal quarantine queue so stewards can distinguish
single-table replay, probe-side join replay, and fan-out/non-replayable quarantines without reading
raw job state.

**Mechanism.** The engine's hash joins have a **build side** (loaded into the hash table —
typically dimensions) and a streamed **probe side** (typically the fact/driving table). Every
output row descends from exactly **one** probe-side row. At quarantine time, capture the
**probe-side source row** instead of the post-join row. Replay then substitutes the probe source
with released rows and the statement re-runs — **joins re-execute against the current build
tables**. This is v2's substitution mechanism extended, not a new replay path: rows still
re-enter the real statement under current rules.

**Why this beats output capture.** The most common join-failure story is a bad *dimension* row
(`Region IN (…)` fails because the dim is missing an entry). With probe-side capture the steward
fixes the **dimension**, releases the fact rows **unchanged**, and replay re-joins and passes.
With output capture that story cannot work — the bad joined value is baked into the captured row,
and replay would need a separate rule-application path that skips the join (a row the pipeline
never derived). Output capture + direct inject remains a rejected-for-now escape hatch for
fan-out joins: it is only reconsidered if real demand appears, and then only marked explicitly as
a manual patch (`__dq_replay_mode = 'inject'`, audited, visible to lineage).

**Correctness gate — observed N:1.** Probe-side replay is sound only when each probe row produces
at most one output row (N:1 lookup joins); under fan-out, one released row would regenerate
sibling output rows that already passed, double-inserting. The gate is checked from **observed
data, per run, for free**: the hash-join build phase sees duplicate build-side keys as it inserts,
so each run records "N:1 verified" (or not) in the manifest. `REPLAY` can therefore always tell
the user exactly why a quarantine set is or isn't replayable. Star-schema enrichment — the
dominant pattern — passes the gate; fan-out joins remain non-replayable.

**Resulting decision tree:** single-table → v2 replay; N:1 join (observed) → v3 probe-side
replay; fan-out join → non-replayable (steered away by the v2 replayability lint).

### V3 implementation slices

1. **Manifest compatibility** — shipped the provenance fields above with defaults so existing manifests
   deserialize as `single-table`.
2. **Probe provenance carrier** — shipped preservation of the original probe/source row through the streaming
   hash-join path separately from the joined output row, so quarantine capture can write source
   columns instead of post-join columns for `probe-join` manifests.
3. **Observed N:1 gate** — shipped detection of build-side duplicate join keys while building the hash table and
   record `JoinObservedN1 = true/false` on the run's manifest. Fan-out remains non-replayable.
4. **Replay substitution** — shipped `REPLAY QUARANTINE` substitution of released rows at
   `ProbeSourceTable` when `ReplayMode = 'probe-join'`, then resume at the recorded label using
   the current build-side tables.
5. **Docs and diagnostics** — shipped `REPLAY QUARANTINE` reference/guide updates and the fan-out
   non-replayable diagnostic. Portal queue copy already surfaces the manifest fields; no embedded
   help resource exists in this tree for a separate hover update.

---

## Future direction — nested-script replay via recorded call stacks (direction only; not designed)

Lifts v2.0's entry-script restriction. Not designed; recorded so the v2 manifest stays
compatible (it already captures the full call chain).

**Mechanism.** At quarantine time, record the `RUN SCRIPT` call stack **with each frame's
evaluated argument values** (`RunScriptStatement.Parameters`). Replay descends the recorded
stack — invoking each script with its **recorded** arguments rather than re-evaluating parent
preamble — then resumes at the label in the innermost script. This generalizes what a job
already is: "run this script with these parameters" (`CreateJobStatementHandler.cs:55`).
Parameters become the child's honest input contract; a child that depends on parent-created
temp tables or connections outside its parameters fails cleanly at replay, exposing that the
script was never actually modular.

**Known constraints to design around:**

- **Ambiguous stacks**: the same child invoked more than once in a run (loop, or multiple
  parents) with different arguments, writing to one quarantine table, yields multiple distinct
  stack+argument contracts for the same released rows. The manifest detects this (multiple
  tuples per quarantine target per run) and marks the set non-replayable rather than guessing.
- **Downstream propagation weakens**: stack-descent replay completes the innermost section but
  never returns to ancestors' post-`RUN SCRIPT` statements. Honest v-next answer: descent replay
  plus "downstream rides the next scheduled run" (or an explicit orchestrator job trigger); full
  path-resume through the root is the more ambitious variant, deferred until demand is proven.

---

## Documentation & LSP (definition of done, per slice)

`docs/reference/` is the embedded runtime help — filenames are the lookup keywords users search by.
Each slice ships its docs and LSP support **in the same PR** as the feature:

- **Slice B**: reference entry for the `WEBHOOK` connection type (options, `FORMAT` payloads, egress-policy behavior, `SECRET:` usage); connector listed in the docs library map if applicable.
- **Slice A**: reference entries for the `EXPECT` column clause (full rule grammar, actions, defaults) and the `ON FAILURE` clause; guide-level "data quality rules" walkthrough whose examples quarantine to **durable** tables; documented limitations (spill-once cost). LSP: rule completions after `EXPECT` and action completions after `ON FAILURE`, diagnostics surfaced from the lint rules. (Rev 11: the comment-strippability residual is gone, and "one action per column" was never the constraint the numbering implied — clauses repeat.)
- **Slice C**: reference entry for `ASSERT JOB` (predicates, `HISTORICAL` semantics incl. cold start, the `ON FAILURE` action blocks); administration-guide note on the persisted DQ metrics columns. LSP: `ASSERT JOB` grammar in completions/diagnostics.
- Stewardship docs: note that `expect`/`fail` appear in the tag catalog as **derived** entries projected from the `EXPECT` clauses, and what the per-run DQ metrics mean for stewards.

## Sequencing

**B → A → C** (B is independent and small — ship it first for immediate standalone alerting value while A, the big slice, is in flight; C's `ALERT` depends on B and C's metrics depend on A's collector):

1. **Slice B** — webhook connector (+ egress enforcement + docs/LSP).
2. **Slice A** — column rules end-to-end (parser, rule DSL, validators, runtime, quarantine, metrics collector + persisted DQ outcomes, docs/LSP).
3. **Slice C** — `ASSERT JOB` + HISTORICAL + the Engine/Orchestrator metrics seam (+ docs/LSP).
4. **v2 remediation** follows in a later release — its manifest lives in the orchestrator state store and it reuses C's seams and B's alerting.

Built on `release/v0.17.0` (feature branches off the active release branch; no direct commits to main/dev).

## Determinism & edge cases

- **`UNIQUE_FIRST/LAST` ties** on the order key within a duplicate group are ambiguous — the keeper is chosen by a full-row deterministic tiebreak; the linter may warn when the `BY` key isn't provably unique.
- **Comment-strippability: retired (Rev 11).** Rules are grammar, so no formatter, comment-stripper, or copy-paste through another tool can remove enforcement — a tool that mangles them produces a syntax error. The Rev 1–10 tripwire survives only as a routing-completeness check (decision 5), and the `WARN`/`THROW`-only residual it could not cover no longer exists. Descriptive tags remain strippable by design; that is the whole point of the distinction.
- **QUARANTINE schema**: the first write fixes the target schema (the statement's **pre-projection input columns** + `__dq_*`); later rows must conform. Durable targets must either not exist (auto-create where the connector supports it) or match.
- **Quarantine inherits governance**: quarantined rows are copies of raw failing data — if a source column carries `@pii` (or other stewardship tags), the quarantine target holds that data too. v1 propagates the source columns' stewardship tags to the quarantine target's lineage/stewardship metadata so PII in quarantine is visible to governance, and docs call out that quarantine tables need the same access controls as their sources. Retention/purge policy for quarantine tables is the operator's responsibility (documented).
- **Spill-once cost**: statements with UNIQUE rules pay one extra disk write/read of the input stream; respect existing spill thresholds and the `MemoryGovernor`; guard with the perf-budget scripts. Statements without UNIQUE rules never spill for DQ.
- **Metrics collector cost**: quarantine/warn/row tallies are free with validation; per-cell null checks occur only for columns registered by `ASSERT JOB` predicates.
- **Test hygiene**: `ConnectorRegistry.Instance` is a mutable global — new connector tests must isolate/reset to avoid order-dependent flakiness. Numeric assertions use the `m` suffix (INT/BIGINT store as `decimal` at runtime).

## Testing strategy

- **Unit**: `ColumnRuleParser` (comma-in-regex, `IN` lists, `UNIQUE_FIRST BY`, `UNIQUE WITH` tuples, `EXISTS IN t(c)`, `EXPR` predicates, malformed → error); each rule's pass/fail; **NULL skips every rule except NOT NULL**; case-sensitivity for MATCHES/IN/EXISTS IN; decimal compares; **NonBacktracking-incompatible regex (backreference/lookaround) → lint Error**; WARN aggregation (count + capped samples, single diagnostic per rule/column); **`@pii` column samples masked in diagnostics and alert payloads**.
- **Parser**: column `EXPECT` clauses (single, repeated on one column, with and without `ON FAILURE`, with and without `AS <alias>`); statement `ON FAILURE` clauses (single/multiple/`TO`); a column-level action followed by `TO`/`WITH` → `SyntaxException` naming the statement clause; `ASSERT JOB` grammar incl. stacked action blocks and `QUARANTINE` rejected there; `UNIQUE_FIRST` without `BY` → `SyntaxException`; **AST round-trip**: every new clause survives `AstSerializer` and re-parse.
- **Linter**: `QUARANTINE` elected on a nested SELECT; QUARANTINE without target; **quarantining statement without an enclosing section label**; **statement-level `ON FAILURE` block elected by no column (symmetric check)**; **`/* @expect: … */` written as a tag → Error pointing at the `EXPECT` clause**; **`ASSERT JOB` predicate naming a column no sink writes → Error (decision 15)** — all Errors; `#temp` quarantine target → Info. Malformed rules are a parse diagnostic before lint runs.
- **Engine**: THROW/WARN/QUARANTINE end-to-end incl. `__dq_*` columns, projected-value semantics, and **pre-projection input-row capture** (quarantine row carries input columns absent from the projection); UNIQUE over a dataset above the spill threshold (deterministic, single source read verified with a read-counting fake source); zero-rules ⇒ no extra pass; pushdown-pin regression test.
- **Metrics**: collector tallies (rows/nulls/quarantined/warned) incl. write-only sink; multi-sink `NULL_PERCENT` ambiguity error; persisted DQ metrics round-trip on SQLite **and** PostgreSQL history stores (additive migration).
- **Connector**: webhook sink vs a mock HTTP endpoint; payload per FORMAT; SECRET redaction in logs/errors; **egress-denied host and denied redirect hop are rejected**.
- **Job**: HISTORICAL math with seeded `JobHistory`; **cold-start skip-with-warning below `MinHistoryRuns`**; `QUARANTINE_PERCENT` end-to-end; notification fires; `ON FAILURE THROW` fails the run and `ON FAILURE NOTIFY` alone does **not**; `WARN_PERCENT = 0` + `THROW` reproduces the old `FAIL_ON_WARN` behaviour including the empty-run case; clean error when no metrics provider.
- **Gate**: `dotnet build ETL-SQL.slnx`; `dotnet test … --filter "Category!=Integration&Category!=Performance&Category!=SLT&Category!=Fuzz"`; perf-budget script.

## Out of scope (v1)

- **The remediation workflow itself** — v1 ships replay-ready quarantine only (design decision 11); the release/replay/discard workflow is v2, designed above.
- **Join-statement replay (v3)** — direction agreed and recorded above (probe-side provenance gated on observed build-side key uniqueness); full design deferred until v2 ships.
- **Nested-script replay** — v2.0 restricts replay to the job's entry script; direction recorded above (stack descent with recorded evaluated arguments); full design deferred until demand is proven.
- **Same-weekday / seasonal HISTORICAL baselines** (v2; `JobHistoryDailySummary` already provides the data).
- **`FRESHNESS(<col>) < <interval>` predicate** for `ASSERT JOB` (v2) — data-recency checks are table stakes across dbt/Soda/Monte Carlo; small predicate on existing infrastructure.
- **`WITHIN <n> SIGMA OF HISTORICAL`** (v2) — stddev-based tolerance that self-tunes per job, matching Deequ/Elementary practice; nearly free since run history is already stored.
- **Alert-state dedup + recovery notifications** (v2) — alert on pass→fail and fail→pass *transitions* with an optional re-alert interval, using the persisted per-run DQ state; prevents a nightly-failing job from posting to Slack forever.
- **Qualified `NULL_PERCENT(target.col)`** for multi-sink runs (v1 errors on ambiguity).
- **Connector-side retention for durable connectors beyond SQLite** (demand-triggered).
- **Deep Governance-dashboard findings integration** (a follow-on that reuses the lineage/stewardship read side; the persisted per-run DQ metrics from §6 are its designed feed).

---

## Rev 11 — surface redesign (2026-08-30)

One principle, applied twice: **comments describe, syntax executes; one action vocabulary
everywhere.** The two changes are independent in the code and dependent in the reasoning — shipping
only the first would leave `ASSERT JOB` speaking a different dialect of the same feature.

### What changes

| | Before (v1, Rev 1–10) | After (Rev 11) |
| :--- | :--- | :--- |
| Column rule | `/* @expect: 'NOT NULL'; @fail: 'THROW'; */` | `EXPECT NOT NULL ON FAILURE THROW` |
| Several rules on a column | `@expect_1`/`@fail_1`, `@expect_2`/`@fail_2` | repeat the clause |
| Combining rules | top-level comma | `AND` (the comma separates columns) |
| Rule values | quoted string, SQL-doubled inside (`'IN (''NA'')'`) | ordinary tokens; only `MATCHES` takes a literal |
| Job severity | `ON CRITICAL_FAILURE THROW` | `ON FAILURE THROW` |
| Job "any warn fails" | `WITH (FAIL_ON_WARN = TRUE)` | `WARN_PERCENT = 0` + `ON FAILURE THROW` |
| Job advisory / notify | default; `ON FAILURE NOTIFY x` | `ON FAILURE WARN` (still the default); `ON FAILURE NOTIFY x` |
| Predicate on an unwritten column | runtime skip + warning ⇒ **assertion passes** | lint error at author time |
| Catalog rule label | `rule_tag` = `@expect_1` | `rule_clause` = `EXPECT #2` |

### What deliberately does not change

The redesign is a surface change; the machinery underneath is untouched, and reviewers should be
able to confirm that quickly:

- The rule grammar and semantics — NULL-skip, `CASE_SENSITIVE`, decimal compares, `BETWEEN`
  expressions, `EXISTS WITH` arity, `LENGTH` lowering, NonBacktracking regex.
- `ON FAILURE` routing at statement level, `TO` targets, `RETENTION`, `HANDLING = SCRIPT | STEWARD`.
- Quarantine capture (pre-projection row, `__dq_*` columns, section-label requirement), the replay
  manifest, `REPLAY QUARANTINE`, and every v2/v3 design above.
- The metrics collector, persisted per-run DQ outcomes, `HISTORICAL` math, cold-start rules, and
  alert transition/dedup behaviour.
- Steward visibility — same catalog entries, same lineage, same dashboard, fed by projection.

### No compatibility shim

The v1 surface has no deployed users, so the tag form and the deleted `ASSERT JOB` clauses are
**removed outright**. A shim would be worse than the break: it would keep the strippable path alive,
keep two ways to say one thing, and have to be removed later anyway. The one concession is
diagnostic, not functional — `EXPECT` clauses written as tags produce a lint **error** naming the
`EXPECT` clause, so an author working from an old example is told what to write instead of watching
their rules do nothing.

### As-built notes on the redesign

Three things the implementation settled that the design above did not anticipate:

1. **`EXPECT` needed an explicit alias guard.** The design assumed a reserved token could not be
   read as an implicit column alias. `Parser.IsIdentifier` is deliberately permissive — it lets most
   keywords serve as aliases — so `SELECT c EXPECT NOT NULL` aliased the column `EXPECT` and dropped
   the rule silently. The implicit-alias branch of `ParseSelectColumn` now excludes `TokenType.EXPECT`
   explicitly. `SELECT c AS EXPECT` still works.
2. **A top-level `AND` unrolls into independent rules.** The comma form produced one rule per
   segment, each reporting its own failures; `AND` producing a single merged `AndRule` would have
   coarsened `__dq_rule` and the catalog listing. Both the clause parser and the read-side string
   parser unroll, so the engine and the catalog agree.
3. **`IParser.SliceSource` exists so a rule can quote itself.** `ColumnRule.Text` is sliced from the
   source by token offsets, which is what makes `__dq_rule` and every diagnostic read back exactly
   as written. When a caller builds a parser from tokens alone, it rebuilds from the tokens instead
   — re-quoting string literals so the result is still valid script text.

### Change inventory

Roughly 90 files reference the old surface; the shape of the work, by area:

- **Core parser/AST** — select-list parsing plus the new `ColumnExpectClauseParser`,
  `SelectColumn.Expectations`, `AssertJobStatement`, `FlowParser.ParseAssertJob` (delete the
  `WITH (FAIL_ON_WARN …)` and `ON CRITICAL_FAILURE` branches), `ExpressionParser` entry points at
  comparison and additive precedence, and `AstSerializer` for both statements.
- **Analysis** — `ColumnRuleValidationRule`, `QuarantineBoundaryRule`, the new `JobMetricColumnRule`,
  and completions (`LanguageMetadata` drops `CRITICAL_FAILURE` and gains the rule keywords).
- **Engine** — `AssertJobStatementHandler` (single failure path), `ColumnQualityValidator` and
  `SelectStatementHandler` where they read tag metadata, `ShowDataQualityRulesStatementHandler`.
- **Steward projection** — `ColumnExpectProjection` feeding `LineageManager`; `StewardshipTagCatalog`
  keeps the definitions the read side resolves.
- **Tooling** — `PipelineGenerator`, the LSP `LanguageService`, the Portal's data-quality views, the
  VS Code extension README and `data_spec_parser_instructions.md`, and `docs/spec-import/` (the
  spec-import prompt teaches the syntax to a model, so a stale copy keeps generating the old form).
- **Tests** — ~24 files, including the rule-grammar, `ON FAILURE`, `ASSERT JOB`, runtime, lint,
  completion, and AST round-trip suites, plus the `quality_routing.etest` corpus file.
- **Docs** — ~35 files, `docs/grammar.ebnf` included. Reference docs are the embedded runtime help,
  so a missed page is a user reading a syntax that no longer parses.

---

## As-built deviations (v1, shipped 2026-07-25)

Four places where the implementation intentionally departs from the design above. Each is a
recorded decision, not an oversight — do not "fix" them without reading the reasoning.

1. **`JobMetricsCollector` was folded into `DataQualityReport`, not shipped separately.**
   §6 specified a distinct collector class. In practice the column-rule validator already
   accumulates exactly the tallies the collector needed (`RowsValidated`, `RowsQuarantined`,
   `RowsWarned`) into `DataQualityReport`, which is on `IExecutionContext` and persists to job
   history. A second accumulator would have duplicated state and created two places for the same
   number to be wrong. Per-column null counts were added to the same type, guarded by
   `TracksNullCounts` so a script with no `NULL_PERCENT` predicate does zero per-cell work — the
   "zero predicates ⇒ zero overhead" property §6 asked for is preserved.

2. **`NULL_PERCENT` has no `HISTORICAL` baseline.** Per-column null fractions are collected
   in-stream but only *aggregate* counts are persisted per run, so there is nothing to average
   across runs. `NULL_PERCENT(col) WITHIN f OF HISTORICAL` parses and is then **skipped with a
   warning** rather than silently passing. Closing this needs a per-column metrics table — see the
   v2 plan.

3. **The UNIQUE duplicate-key map uses hash-partitioned spill rather than `ExternalAggregateEngine`.**
   §4 specified `ExternalAggregateEngine`; the shipped scale-hardening path writes projected UNIQUE
   key records into spill partitions and reduces one partition at a time. This keeps the source
   single-read guarantee, batches all UNIQUE rule occurrences in one pre-pass, and bounds memory by
   the hottest hash partition while avoiding a wider aggregate-engine contract change.

4. **Connector-side retention is opt-in per data source.** `WITH (RETENTION = …)` prunes
   in-memory/`#temp` targets at end of run. Durable targets prune only when the connector implements
   `IDataQualityRetentionPruner`; SQLite does, and other durable connectors deliberately remain
   demand-triggered rather than inheriting deletes by default.

Two smaller notes: `WARN_PERCENT` was added as a fourth `ASSERT JOB` metric (not in the original
predicate list) because the warn tally was already collected and asserting on it is the natural
companion to `QUARANTINE_PERCENT`; and the local-path pin covers the SQL pushdown paths as well as
the columnar ones, since remote pushdown bypasses local projection just as thoroughly.
