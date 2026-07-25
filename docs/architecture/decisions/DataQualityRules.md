# Column & Job Data-Quality Rules — Design Specification

> **Status:** ✅ **v1 SHIPPED** on `release/v0.17.0` (2026-07-25). All three slices are implemented:
> **B** the `WEBHOOK` connector, **A** column rules end-to-end, **C** `ASSERT JOB` + `HISTORICAL`.
> User-facing documentation lives at
> [Data Quality Rules](../../reference/statements/dml/data-quality-rules.md),
> [ASSERT JOB](../../reference/statements/session-control/assert-job.md), and the
> [Validating Data Quality](../../guides/data-quality.md) guide — those are authoritative for
> behavior. This document remains the design record: the decisions and their rationale, plus the
> v2 design below. The quarantine replay manifest foundation, `UPDATE`-time disposition
> enforcement, and `REPLAY QUARANTINE` source-substitution replay are now built; replay leasing,
> and the Portal steward grid remain pending.
>
> **Where the implementation deliberately differs from this spec** (see "As-built deviations" at the
> end for the reasoning): the §6 `JobMetricsCollector` was folded into `DataQualityReport` rather
> than shipped as a second accumulator; `NULL_PERCENT` has no `HISTORICAL` baseline in v1; the
> UNIQUE duplicate-key map is in-memory rather than built on `ExternalAggregateEngine`; and
> retention pruning applies only to engine-managed targets.
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
> the existing resume-at-label machinery. Successful replay flips consumed rows to `replayed`;
> replay lease fencing remains a follow-up slice.

## Goal

Extend the engine's verification surface from **schema** (`EXPECT SCHEMA … ON DRIFT WARN`) and
**boolean assertions** (`ASSERT`) to **column-value** and **job-metric** data quality:

- **Column rules** declared inline on SELECT columns via tags — `SELECT UserId /* @expect: 'NOT NULL'; @fail: 'THROW'; */` — with pluggable fail actions (`THROW` / `WARN` / `QUARANTINE`).
- **Job rules** declared with `ASSERT JOB` over run metrics collected **in-stream during execution** — e.g. `ROW_COUNT WITHIN 0.2 OF HISTORICAL`, `NULL_PERCENT(Email) < 0.02`, `QUARANTINE_PERCENT < 0.01` — with alerting.
- A **webhook** connector so failures can notify Slack / Teams / generic endpoints.

This closes the data-quality half of the stewardship+quality sprint; DQ failures are conceptually
governance findings and are designed to feed the same lineage/stewardship read side the Governance
dashboard already consumes.

**Why tags, not new column syntax:** `@expect`/`@fail` are registered stewardship tags, so the
rules themselves are **steward-visible governance metadata** — they surface everywhere tags already
do (tag catalog, lineage/stewardship read side, Governance dashboard). A data steward can see *what
rules protect a column* without reading engine internals, and — because DQ outcomes are persisted
per run (§7) — *what impact those rules had*. Rules-as-tags plus metrics-as-history is the complete
steward picture; either half alone is not.

## Current state (verified against code)

- **`ASSERT` is boolean-only, hard-fail.** `AssertStatement(Expression Condition, Expression? Message)` (`src/ETL-SQL.Core/Ast.cs:1327`), parsed by `FlowParser.ParseAssert` (`src/ETL-SQL.Core/Parser/Components/FlowParser.cs:279`), dispatched via `_dispatchMap[TokenType.ASSERT]` (`StatementParser.cs:79`), handled by `AssertStatementHandler` (throws `ExecutionException`). No severity, no action clause, no column/job awareness.
- **Trailing-action prior art exists.** `EXPECT SCHEMA … ON DRIFT WARN` (`ExpectSchemaStatement { bool WarnOnDrift }`, `Ast.cs:1343`) parses its trailing clause at `FlowParser.cs:331` by matching `TokenType.ON` then `DRIFT`/`WARN` as **contextual identifiers**. `ON` is a real keyword token; `FAILURE`/`WARN`/`THROW`/`QUARANTINE` can be matched the same way. `ExpectSchemaStatementHandler.cs:125` is the WARN-vs-THROW pattern to reuse. No statement currently parses *multiple stacked* action blocks.
- **The comment-tag pipeline already supports the proposed syntax with no lexer/parser change.** `/* @tag: val; */` and `-- @tag: val` lex to a single `TokenType.COLUMN_TAG` (`Lexer.ReadCommentOrTag`, `Lexer.cs:273`); plain comments are discarded. `Parser.ParseMetadataTags` (`Parser.cs:1865`) splits on `;`, parses `@name: value` **keeping the value verbatim including quotes**, and binds trailing tags positionally to the preceding column (`Parser.cs:1777`). `SelectColumn(Expression, alias, Dictionary<string,string>? metadata)` (`Ast.cs:77`) is the landing spot — `@expect`/`@fail` arrive as `Metadata["expect"]` / `Metadata["fail"]`.
- **Tag governance + validation seams exist.** `StewardshipTagCatalog.Definitions` (`src/ETL-SQL.Core/Common/StewardshipTagCatalog.cs:40`) is the first-class vocabulary (owner/steward/pii/…); `UnknownTagLintRule` flags anything absent from it. `TagValueValidationRule` validates values; `TagGovernanceRuntimePolicy` (`src/ETL-SQL.Engine/Handlers/TagGovernanceRuntimePolicy.cs`) reads tag values and throws `ExecutionException` at runtime — the closest analog to `@fail`.
- **Severity channels already present.** `ExecutionException` carries an int `Severity` (default 16) + `ErrorNumber`/`State`/`Line` (`src/ETL-SQL.Core/Common/Exceptions/ETLException.cs`); `DiagnosticSeverity { Error, Warning, Info, Hint }` flows into `ExecutionResult.Diagnostics` (`ExecutionResult.cs:16`) — the right surface for a structured non-fatal WARN.
- **A real streaming row pipeline exists.** Query execution is `IAsyncEnumerable<Row>`; the projection step `ProjectRows(...)` in `src/ETL-SQL.Engine/Engines/SelectExecutionEngine.cs:724` (invoked at `:608`/`:706`) is the natural inline validation hook. Write-side precedent: `InMemoryDataSource.WriteBatchesCore` calls `IDataValidator.ValidateCheckConstraint(Expression, Row)` per row (`DataSources.cs`; impl `DataConstraintValidator.cs`).
- **Pushdown runs upstream of the hook.** `SemiJoinPushdownOptimizer` and `PredicatePushdownOptimizer` rewrite the plan before execution (`SelectExecutionEngine.cs:65-69`). They move *filters* toward sources; output rows still flow through `ProjectRows`. Column rules validate **output rows**, so upstream predicate pushdown does not change rule semantics — but any current or future path that bypasses local projection entirely must be **pinned to the local path** when rules are present (§4).
- **Disk-spilling engines exist for the UNIQUE pass.** `ExternalAggregateEngine.ApplyAggregationExternal(groupBy, …, having, …)` (`Engine/Engines/ExternalAggregateEngine.cs:54`) and `ExternalDistinctEngine` (hash-partition; equal keys co-located). Spill thresholds ~100k rows (`JoinSpillThreshold`), 1M for temp tables (`TempTableSpillThresholdRows`), plus a process-wide `MemoryGovernor` ceiling.
- **Temp-table materialization for QUARANTINE exists.** `#name` targets auto-create an `InMemoryDataSource` on first write (`InsertStatementHandler.cs:36`); `INSERT` streams via `WriteBatches(IAsyncEnumerable<DataTable>, append:true)` (`PerformBatchTransfer`). Mid-query temp writes are already proven by the REST connector's `RESPONSE_TABLE=#name` capture.
- **Connectors support sink-only, and outbound HTTP already exists — with egress enforcement.** `IConnector`/`IConnectorRegistry` (`Core/Data/DatabaseConnectors.cs`); most members are default-implemented. Sink-only is idiomatic (`SmtpConnector`/`SmtpDataSource` — `ReadBatches`→`yield break`; Kafka). `RestConnector` (`Connectors/Rest/RestConnector.cs`) does templated outbound POST/PUT with retries and idempotency, and `RestDataSource` (`RestDataSource.cs:34-53`) validates every target host against the egress allowlist (`SecurityService.ValidateHost`), re-validates **every redirect hop** (`:1344`, `:1398`), and disables `UseProxy` so an ambient proxy cannot route around the controls. The webhook connector **must reuse this exact path**. Registration is one `AddSingleton<IConnector,…>` line each in `TUI/App/TuiDependencyInjectionSetup.cs` and `Orchestrator/DependencyInjectionExtensions.cs`, plus `registry.Register(...)` in `LanguageServer/Program.cs`. `CreateConnectionStatementHandler` resolves any registered type — no change needed.
- **Historical run metrics are persisted.** `SQLiteJobHistoryStore` `JobHistory` records per-run `RowsProcessed` (+ `PeakMemoryBytes`, `CpuTimeSeconds`); `GetHistoryAsync(job, limit)` and daily rollup `JobHistoryDailySummary.TotalRows` via `GetJobHistoryDailyAsync` (interface `IJobHistoryStore`, `Core/Data`). Current-run value is `ExecutionResult.RowsProcessed`. **Seam:** this lives in `ETL-SQL.Orchestrator`; statement handlers run in `ETL-SQL.Engine` against `IExecutionContext`, which does not expose it today.

## Design decisions (locked)

1. **Full scope**: column rules **+** `ASSERT JOB` **+** webhook connector.
2. **Rules are steward-facing governance metadata.** `@expect`/`@fail` are first-class catalog tags precisely so stewards can see which rules protect which columns through the existing stewardship surfaces; per-run DQ metrics (decision 8) complete that picture with observed impact.
3. **`UNIQUE_FIRST`/`UNIQUE_LAST` require an explicit `BY <key>`** — reject without one. Source/spill/parallel order is not stable, so "first" is otherwise non-deterministic.
4. **`QUARANTINE` is legal only at a sink/materialization boundary** (top-level SELECT, `INSERT … SELECT`, `SELECT … INTO`) — a parse/lint error on nested subquery/CTE columns, because it is a filter with a side effect that would silently change downstream row counts.
5. **Rule tags are first-class and validation is symmetric.** `expect`/`fail` and their numbered variants (e.g. `@expect_1`/`@fail_1`, `@expect_2`/`@fail_2`) are registered in the tag catalog; malformed/unknown rules are **hard errors** (lint + parse), never silently ignored. This allows mapping multiple distinct rules and different actions to a single column. Symmetry: `@fail: 'QUARANTINE'` with no matching `ON FAILURE QUARANTINE TO …` clause is a hard error, **and an `ON FAILURE <ACTION>` clause with zero matching `@fail` rules is equally a hard error**. A formatter or tool that strips comments therefore breaks the script *loudly* (orphaned `ON FAILURE` clause) instead of silently disabling enforcement. This is the primary mitigation for the "comments are strippable" failure mode; `WARN`/`THROW`-only rules with no `ON FAILURE` clause remain silently strippable — a documented residual limitation.
6. **Job metrics are collected in-stream during the run, never by post-run re-scan.** A metrics collector wraps the sink-side row stream and computes `ROW_COUNT`, per-column null counts, and quarantine/warn tallies in the same pass. This makes `NULL_PERCENT` near-free, works for **write-only sinks** (Kafka, webhook, SMTP) where a post-run query is impossible, and produces the persisted DQ metrics as a by-product.
7. **The UNIQUE pre-pass is spill-once, single source read.** When any UNIQUE rule is present, the input stream is materialized once to spill storage; both the duplicate-key pre-pass and the main validation pass read from the spill. The source is **never read twice** — a second read is impossible or inconsistent for non-rewindable sources (Kafka, paginated REST), and even for rewindable sources two reads can observe different data.
8. **DQ outcomes are persisted per run.** Rows quarantined, rows warned, and per-rule failure counts are recorded on the run's job-history record and exposed on `ExecutionResult`. Without this there is no trend visibility and `ASSERT JOB` could never assert on quarantine rate — the most natural job-level DQ metric.
9. **The webhook connector inherits REST egress enforcement wholesale.** Arbitrary outbound POST with `SECRET:` access is otherwise an exfiltration primitive. Host validation, per-redirect-hop re-validation, and the proxy-disabled handler are mandatory, and the connector must satisfy `docs/architecture/standards/Connectors_Standards.md` (10 inviolable rules + checklist).
10. **Documentation and LSP support are part of each slice's definition of done**, not a trailing phase. `docs/reference/` is the embedded runtime help (filenames are lookup keywords) — new surface that ships without reference docs is invisible to users at the point of use.
11. **v1 quarantine is replay-ready by construction.** Quarantine captures the **pre-projection input row**, requires an **enclosing section label**, and carries `__dq_status`/`__dq_row_id`/`__dq_run_id` plus a **reserved, always-NULL `__dq_origin_row_id`** from day one — so the v2 remediation workflow (label replay with source substitution, designed below) needs no breaking change to quarantine tables written by v1. Because the target schema is fixed on first write (§ Determinism), the v2 re-quarantine linkage column must exist in v1's schema even though only v2 ever populates it; adding it later would break v1-created tables.
12. **Quarantine table schema drift is verified, not ignored.** If the target schema of a durable quarantine table does not match the incoming pre-projection schema, the engine will attempt an additive migration (adding columns that are missing) or fail validation safely if data types are incompatible, alerting the steward.
13. **Quarantine and warn targets support configurable data retention.** Both `ON FAILURE QUARANTINE TO` and `ON FAILURE WARN TO` clauses accept a retention configuration (e.g. `WITH (RETENTION = '30 DAYS')`) to allow the engine to prune older records automatically. Retention is especially critical for warn tables, which have no lifecycle state machine to provide natural pruning.

---

## Proposed surface

### Column rules

```sql
-- Actions bind by name: @fail picks the action; the trailing ON FAILURE
-- clause supplies that action's routing target. A column can contain multiple
-- numbered rule/action pairs (e.g. @expect_1, @fail_1). 
-- A quarantining statement must sit inside a section label.
import_users:
SELECT
    UserId   /* @expect: 'NOT NULL'; @fail: 'THROW'; 
                @expect_1: 'UNIQUE'; @fail_1: 'QUARANTINE'; */,
    Email    /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */,
    Age      /* @expect: '>= 0'; @fail: 'WARN'; 
                @expect_1: '<= 120'; @fail_1: 'WARN'; */,
    Region   /* @expect: "IN ('NA','EMEA','APAC')"; @fail: 'QUARANTINE'; */,
    RegionId /* @expect: 'EXISTS IN dim_region(Id)'; @fail: 'QUARANTINE'; */,
    EventId  /* @expect: 'UNIQUE_FIRST BY LoadedAt'; @fail: 'QUARANTINE'; */
INTO clean_users
FROM raw_users
ON FAILURE QUARANTINE TO quarantine_users WITH (RETENTION = '30 DAYS')
ON FAILURE WARN TO warning_log_users WITH (RETENTION = '30 DAYS')  -- optional; omit TO for diagnostic-only
ON FAILURE THROW;   -- Up to 3 distinct routing targets are allowed
```

- **Rules** (combinable with top-level commas — the most-used forms from the dbt/GE/Soda field, per the competitive review): `UNIQUE`, `UNIQUE WITH (<col>, …)` (composite key declared on one column, unique over the tuple), `UNIQUE_FIRST BY <expr>`, `UNIQUE_LAST BY <expr>`, `NOT NULL`, `MATCHES <regex>`, `IN (<list>)`, `EXISTS IN <table>(<column>)` (relationship/FK check), `EXPR <predicate>` (cross-column boolean over the full row, e.g. `EXPR StartDate <= EndDate`), and numeric `>= <= > < =`.
- **NULL semantics (defined, not implied)**: `NOT NULL` is the only rule that fails on NULL. Every other rule **skips NULL values** (SQL `CHECK`-constraint convention, matching dbt `accepted_values`) — pair with `NOT NULL` explicitly to reject them. Without this rule, every nullable column would double-fail.
- **Rules evaluate against the projected (post-expression) value.** `SELECT UPPER(Email) /* @expect: 'MATCHES …' */` validates the uppercased value; `__dq_value` records that projected value.
- **Actions** (`@fail`): `THROW` (error, `ExecutionException`), `WARN` (row passes through; aggregated diagnostic always emitted; row optionally captured to a warn table), `QUARANTINE` (row removed from output, written to the `TO` target). Default when `@expect` is present but `@fail` is omitted: **`WARN`** (fail-safe, not silent).
- **Numbered Suffixes**: Multiple rule-action pairs are supported on a single column by adding a matching integer suffix (e.g. `@expect_1` pairs with `@fail_1`). The un-suffixed `@expect` pairs with the un-suffixed `@fail`. If an action is omitted for a numbered expectation, it defaults to `WARN`.
- **`ON FAILURE <ACTION> [TO <table>] [WITH (<options>)]`** trailing blocks route each action. Up to three blocks are supported concurrently (`QUARANTINE`, `WARN`, `THROW`). `TO` is **required** for `QUARANTINE` (the row has nowhere else to go) and **optional** for `WARN` (omitting `TO` produces diagnostic-only mode — the aggregated warning fires but no row is written to a table). `THROW` never takes a `TO` target. Symmetric validation (design decision 5) applies: a `QUARANTINE` or `WARN` tag without a matching `ON FAILURE` clause, and a clause without any matching tag, are both hard errors.
- **Retention Options**: Both `ON FAILURE QUARANTINE TO` and `ON FAILURE WARN TO` targets accept `WITH (RETENTION = '<interval>')` (e.g. `'30 DAYS'`). The engine prunes rows older than the interval on each run. Warn tables have no lifecycle pruning beyond retention, so the linter emits a `Diagnostic(Info)` when a `WARN TO` target is declared without a `RETENTION` option, recommending one be set.
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
| `__dq_row_id` | Hash of input row + run_id | ✓ — same hash, same deduplication semantics |
| `__dq_status` | `'quarantined'` (lifecycle: released/replayed/discarded) | **`'warned'` (fixed — no lifecycle transitions; row is already in the target)** |
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
    NULL_PERCENT(Email) < 0.02,
    QUARANTINE_PERCENT < 0.01
)
ON FAILURE ALERT alerts_webhook
ON CRITICAL_FAILURE THROW;
```

### Webhook connection

```sql
CREATE CONNECTION alerts_webhook AS WEBHOOK(URL = 'SECRET:slack_url', FORMAT = 'slack');
```

The webhook is a general-purpose sink: any script can `INSERT INTO` it, not only DQ alerts.

---

## Component design

### 1. Rule model + mini-DSL parser — new `src/ETL-SQL.Core/Quality/`

- `ColumnRule.cs`: abstract `ColumnRule` + `NotNullRule`, `UniqueRule(UniqueMode Mode, Expression? OrderKey, IReadOnlyList<string>? CompositeColumns)`, `MatchesRule(string Pattern)`, `ComparisonRule(CompareOp Op, decimal Value)`, `InListRule(IReadOnlyList<object?> Values)`, `ExistsInRule(string Table, string KeyColumn)`, `ExprRule(Expression Predicate)`. Enums `UniqueMode { All, First, Last }`, `FailAction { Throw, Warn, Quarantine }`. `MATCHES` patterns compile with **`RegexOptions.NonBacktracking`** — a per-row user-supplied regex is otherwise a ReDoS vector that can hang the engine mid-pipeline; the linter rejects constructs NonBacktracking cannot compile (backreferences, lookaround).
- `ColumnRuleParser.cs`: parses the `@expect` string into rules. **Must be a real tokenizer, not `string.Split(',')`** — commas occur inside `MATCHES <regex>` and `IN (a,b,c)`. Strips the outer quotes the tag layer preserves. Parsed rules are cached per `SelectColumn`.

### 2. Trailing `ON FAILURE` clause — Core parser + AST

- Extend `SelectStatement` with `IReadOnlyList<FailureActionClause>? OnFailureActions`; add `FailureActionClause(FailAction Action, string? Target, RetentionInterval? Retention)`.
- Parse after the query body, before `;`, mirroring `ParseExpectSchema`'s `ON DRIFT WARN` (`FlowParser.cs:331`). `QUARANTINE` **requires** `TO <table>`; `WARN` **optionally** takes `TO <table>` (no `TO` = diagnostic-only mode, no row capture); `THROW` never takes `TO`.

### 3. First-class tags + validators — Analysis

- Register in `StewardshipTagCatalog.Definitions` (column scope): `expect` as `String`, `fail` as `Enum` with allowed `THROW`/`WARN`/`QUARANTINE`. (The rule grammar itself is too rich for the value-kind validator — hence a dedicated linter.) Catalog registration is what makes the rules visible on the stewardship/lineage read side.
- `ColumnRuleValidationRule.cs` (model on `TagValueValidationRule.cs`): run `ColumnRuleParser`; `Diagnostic(Error)` on malformed rules, bad regex, `UNIQUE_FIRST/LAST` missing `BY`, unknown action.
- `QuarantineBoundaryRule.cs`: `Diagnostic(Error)` when `@fail: 'QUARANTINE'` is on a non-sink SELECT, when a `QUARANTINE` action lacks a `TO` target, when a quarantining statement has **no enclosing section label** (`SectionLabelStatement`, `Ast.cs:1549` — the label is the v2 replay re-entry point, required from v1), **and — symmetric check — when any `ON FAILURE <ACTION>` clause has no matching `@fail` rule in the statement** (the comment-stripping tripwire). `Diagnostic(Info)` when quarantining to a `#temp` target (recommend durable); `Diagnostic(Info)` when a `WARN TO` target is declared without a `RETENTION` option (warn tables have no lifecycle pruning). The linter may also warn when a `UNIQUE_FIRST/LAST` `BY` key isn't provably unique.

### 4. Runtime enforcement — Engine

- `src/ETL-SQL.Engine/Services/ColumnQualityValidator.cs` (model on `DataConstraintValidator.cs` / `TagGovernanceRuntimePolicy.cs`), invoked by wrapping the `ProjectRows(...)` stream (`SelectExecutionEngine.cs:724`) in a validating async iterator. **Zero rules ⇒ zero overhead** (the path is skipped).
  - **Per-row rules** (NotNull/Matches/Comparison/In/Expr) evaluate inline against the projected value; `EXPR` predicates get the full projected row. NULL values **skip** every rule except `NOT NULL` (see Proposed surface). Honor `SET CASE_SENSITIVE` for MATCHES/IN/EXISTS IN. Numeric compares are **decimal** at runtime. THROW → `ExecutionException`; WARN → aggregated (below); QUARANTINE → divert row.
  - **EXISTS IN** builds its key set once per statement from the referenced table (hash set via the existing spill-aware infrastructure; reference tables are typically dimension-sized), then probes per row. The build honors `SET CASE_SENSITIVE`.
  - **WARN is aggregated, never per-row.** Per-row diagnostics on a 10M-row load with a high failure rate is a diagnostics DoS. The validator keeps, per (rule, column): a failure **count** plus the first **N sample values** (default 10, configurable under `appsettings.json → Engine`), and emits **one** `Diagnostic(Warning)` per (rule, column) at end of stream with count + samples. Per-row detail goes to Debug-level logging only.
  - **PII masking in samples and alerts.** Sample values from a `@pii`-tagged column are **masked** in warn diagnostics, logs, and every alert payload (`ASSERT JOB … ALERT` webhook summaries) — counts stay, values don't. A governance feature must not exfiltrate PII to Slack. The full value is preserved only inside the quarantine table itself, which carries propagated stewardship tags and access controls (see Determinism & edge cases).
  - **UNIQUE rules run over a single spill materialization** (design decision 7). The validating iterator spills the upstream stream once (respecting `JoinSpillThreshold`-class thresholds and the `MemoryGovernor`); the duplicate-key set is built from the spill via `ExternalAggregateEngine.ApplyAggregationExternal(groupBy=[col], HAVING COUNT(*)>1)` (composite `UNIQUE WITH` groups by the column tuple — same engine, multi-column key) — for `UNIQUE_FIRST/LAST BY key` also aggregating `MIN/MAX(orderKey)` per group so only the keeper survives — then the main pass streams from the same spill. Cost is one extra disk write/read of the stream, documented. One pre-pass per unique column in v1 (single-pass batching is a noted optimization).
  - **Rules pin execution to the local path.** Upstream predicate/semi-join pushdown is unaffected (it moves filters, and rules validate output rows), but any plan shape that would bypass local projection entirely is disabled for statements carrying `@expect` rules, with a regression test guarding the pin.
- **QUARANTINE routing**: resolve the `TO` target via `context.ResolveDataSourceAsync` (auto-create for `#temp`), write with `WriteBatches(append:true)`. **The captured row is the pre-projection input row** — every input column the statement saw, available directly in the `ProjectRows` wrapper — not the projected output row. This is what makes v2 replay possible (re-feed the row through the statement) and it is also better for stewards: they fix the *cause* (the source value), not the symptom. Rows are **augmented** with `__dq_rule`, `__dq_column`, `__dq_value` (the projected value that failed), `__dq_reason`, `__dq_ts`, `__dq_run_id`, `__dq_status` (always `'quarantined'` when written — the v2 disposition column, shipped in v1 so remediation never breaks the schema), `__dq_row_id` — a deterministic hash of the captured row content + run id, the stable identity replay-once semantics key on — and a **reserved `__dq_origin_row_id`** written as NULL in v1. The latter is the forward-compat hook for decision 11: v2 replay populates it when an edited-but-still-failing row re-quarantines (linking the new row back to the original `__dq_row_id`), and because the quarantine schema is frozen on first write (§ Determinism), the column must be present in v1-created tables or v2 could not write to them. The engine routes and annotates; the **remediation workflow ships as v2** (designed below) — v1 users remediate by hand against the same schema.
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

- In `FlowParser.ParseAssert`, peek for a contextual `JOB` token → `ParseAssertJob`. AST: `AssertJobStatement(string JobName, IReadOnlyList<JobMetricPredicate> Predicates, string? AlertConnection, bool ThrowOnCritical)`.
- v1 predicates: `ROW_COUNT WITHIN <frac> OF HISTORICAL`, `NULL_PERCENT(<col>) <op> <v>`, `QUARANTINE_PERCENT <op> <v>`, and simple recorded-metric compares. All current-run values come from the `JobMetricsCollector` (§6) — never a re-scan.
- **HISTORICAL** = mean of the last N completed runs' recorded metric (N configurable, default 5) via `IJobHistoryStore.GetHistoryAsync`; `WITHIN f` ⇒ `|cur − base| / base ≤ f`.
- **Cold start is defined, not accidental**: `HISTORICAL` requires a minimum of `MinHistoryRuns` completed runs (default **3**, configurable). Below the minimum, the predicate is **skipped with a `Diagnostic(Warning)`** ("insufficient history: n of 3 runs") — the job's first deployments must not alert-storm. Non-`HISTORICAL` predicates always evaluate.
- **Seasonality is a known v2**: mean-of-last-N will false-positive on weekly load patterns (Monday ≠ Sunday). `JobHistoryDailySummary` / `GetJobHistoryDailyAsync` already exist, so a same-weekday baseline is a cheap follow-on — deliberately out of v1 scope, recorded below so it isn't forgotten.
- **Engine→Orchestrator seam**: new narrow `src/ETL-SQL.Core/Data/IJobMetricsProvider.cs`, implemented in Orchestrator over `IJobHistoryStore`, exposed on `IExecutionContext`. Null in pure-engine/CLI contexts ⇒ `HISTORICAL` predicates fail cleanly ("requires orchestrator history"); collector-backed predicates (`NULL_PERCENT`, `QUARANTINE_PERCENT`, plain `ROW_COUNT` compares) still work everywhere.
- Handler `AssertJobStatementHandler.cs`: on any predicate failure with `ON FAILURE ALERT`, POST a summary through the named webhook — with `@pii`-tagged column values masked (metric values and counts only, never sample data from PII columns); if `ON CRITICAL_FAILURE THROW`, throw after alerting. Webhook delivery failure has its own policy (log + continue by default), independent of `ON CRITICAL_FAILURE`.

---

## v2 — Quarantine remediation (designed; not v1 scope)

Script-first with a Portal front end over the same mechanism. v1 ships the hooks (design
decision 11); v2 ships the workflow.

### Disposition model

`__dq_status` flows `quarantined → released → replayed | discarded`. Stewards **edit rows with
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
`discarded`; `released` rows may move to `replayed` or `discarded`; `replayed` and `discarded` are
terminal except idempotent self-updates. Rows with status `warned` cannot change status.

### Replay = resume-at-label + source substitution

At quarantine time the engine writes a **manifest** to the **orchestrator state store**:
*(job, script, section label, substituted source table, quarantine target, replayable flag,
input-schema fingerprint)*. The source binding is recorded explicitly at quarantine time — never
inferred positionally ("first table in the section") at replay time, which would break silently
when someone adds an earlier table read to the section.

**As built so far:** orchestrator-hosted runs persist that manifest through the
`IJobMetricsProvider`/job-state seam on first quarantine write. The stored payload includes the
captured input column list as well as the fingerprint, and records a non-replayable reason for
unsupported shapes such as joins. The replay statement now consumes that manifest; replay lease
fencing remains the next remediation slice.

`REPLAY QUARANTINE <quarantine_table>;` (script statement; the Portal **Replay** button enqueues
the same as an orchestrator run) resolves the manifest and re-runs the job via the existing
resume machinery (`Evaluator.ResumeLabel`, `Evaluator.cs:1009`) with one substitution: the
recorded source table is fed from `<quarantine_table> WHERE __dq_status = 'released'` with the
`__dq_*` columns stripped. Because released rows re-enter the **current statement**:

**As built so far:** the statement resolves the manifest, fails clearly when the manifest is missing
or marked non-replayable, builds an in-memory source stream from released rows with `__dq_*`
evidence columns stripped, and resumes the recorded section label through the existing evaluator
resume path. After a successful replay, it flips consumed rows from `released` to `replayed`.
It does not yet take the replay lease.

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
ASSERT JOB import_events (FRESHNESS(EventTime) < '2 HOURS') ON CRITICAL_FAILURE THROW;
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
zero standard deviation (a perfectly stable job) collapses the band to equality, so it falls back
to the relative-tolerance path with a warning rather than failing every run.

### Seasonality — same-weekday baselines

Mean-of-last-N false-positives on weekly patterns: a Monday run compared against a window
containing weekend runs drifts for entirely healthy reasons. `JobHistoryDailySummary` and
`GetJobHistoryDailyAsync` already exist, so a `WITHIN f OF HISTORICAL BY WEEKDAY` variant is a
filter on the history query rather than new machinery. Deferred behind the three items above
because it only matters once a job has months of history.

---

## v2 — alert quality (shipped)

Once `ASSERT JOB … ALERT` is in real use, the failure mode is **alert fatigue**: a job that fails
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
| Spill-aware UNIQUE key map | A UNIQUE rule on a very high-cardinality key exhausts memory on a constrained host | Build the duplicate-key groups through `ExternalAggregateEngine` (`GROUP BY key HAVING COUNT(*) > 1`, with `MIN`/`MAX(orderKey)` for `UNIQUE_FIRST/LAST`), as §4 originally specified. The row stream already spills once; only the map changes. |
| Single-pass UNIQUE batching | A statement declares UNIQUE on several columns and the per-column pre-passes dominate runtime | One pre-pass collecting all unique keys simultaneously instead of one pass per column. |
| Connector-side retention | Operators ask why `WITH (RETENTION = …)` does not prune their durable quarantine table | Issue a bounded delete through the connector for targets that support it, behind an explicit opt-in — the engine currently declines to run DELETEs against a user-owned table, and that caution should be lifted deliberately, not by default. |

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

## v3 direction — join-statement replay via probe-side provenance (direction only; not designed)

The agreed direction for lifting v2's single-table restriction. Not yet a full design — recorded
so the v2 hooks stay compatible with it.

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
- **Slice A**: reference entries for `@expect` / `@fail` tags (full rule grammar, actions, defaults) and the `ON FAILURE` clause; guide-level "data quality rules" walkthrough whose examples quarantine to **durable** tables; documented limitations (comment-strippability residual, spill-once cost, one action per column). LSP: tag-name/value completions for `expect`/`fail`, diagnostics surfaced from the new lint rules.
- **Slice C**: reference entry for `ASSERT JOB` (predicates, `HISTORICAL` semantics incl. cold start, `ALERT`/`CRITICAL_FAILURE`); administration-guide note on the persisted DQ metrics columns. LSP: `ASSERT JOB` grammar in completions/diagnostics.
- Stewardship docs: note that `expect`/`fail` appear in the tag catalog and what the per-run DQ metrics mean for stewards.

## Sequencing

**B → A → C** (B is independent and small — ship it first for immediate standalone alerting value while A, the big slice, is in flight; C's `ALERT` depends on B and C's metrics depend on A's collector):

1. **Slice B** — webhook connector (+ egress enforcement + docs/LSP).
2. **Slice A** — column rules end-to-end (parser, rule DSL, validators, runtime, quarantine, metrics collector + persisted DQ outcomes, docs/LSP).
3. **Slice C** — `ASSERT JOB` + HISTORICAL + the Engine/Orchestrator metrics seam (+ docs/LSP).
4. **v2 remediation** follows in a later release — its manifest lives in the orchestrator state store and it reuses C's seams and B's alerting.

Built on `release/v0.17.0` (feature branches off the active release branch; no direct commits to main/dev).

## Determinism & edge cases

- **`UNIQUE_FIRST/LAST` ties** on the order key within a duplicate group are ambiguous — the keeper is chosen by a full-row deterministic tiebreak; the linter may warn when the `BY` key isn't provably unique.
- **Comment-strippability (residual)**: the symmetric hard-error (design decision 5) makes stripped `QUARANTINE` rules fail loudly via the orphaned `ON FAILURE` clause; `WARN`/`THROW`-only rules with no trailing clause still vanish silently if a downstream tool strips comments. Documented as a known limitation.
- **QUARANTINE schema**: the first write fixes the target schema (the statement's **pre-projection input columns** + `__dq_*`); later rows must conform. Durable targets must either not exist (auto-create where the connector supports it) or match.
- **Quarantine inherits governance**: quarantined rows are copies of raw failing data — if a source column carries `@pii` (or other stewardship tags), the quarantine target holds that data too. v1 propagates the source columns' stewardship tags to the quarantine target's lineage/stewardship metadata so PII in quarantine is visible to governance, and docs call out that quarantine tables need the same access controls as their sources. Retention/purge policy for quarantine tables is the operator's responsibility (documented).
- **Spill-once cost**: statements with UNIQUE rules pay one extra disk write/read of the input stream; respect existing spill thresholds and the `MemoryGovernor`; guard with the perf-budget scripts. Statements without UNIQUE rules never spill for DQ.
- **Metrics collector cost**: quarantine/warn/row tallies are free with validation; per-cell null checks occur only for columns registered by `ASSERT JOB` predicates.
- **Test hygiene**: `ConnectorRegistry.Instance` is a mutable global — new connector tests must isolate/reset to avoid order-dependent flakiness. Numeric assertions use the `m` suffix (INT/BIGINT store as `decimal` at runtime).

## Testing strategy

- **Unit**: `ColumnRuleParser` (comma-in-regex, `IN` lists, `UNIQUE_FIRST BY`, `UNIQUE WITH` tuples, `EXISTS IN t(c)`, `EXPR` predicates, malformed → error); each rule's pass/fail; **NULL skips every rule except NOT NULL**; case-sensitivity for MATCHES/IN/EXISTS IN; decimal compares; **NonBacktracking-incompatible regex (backreference/lookaround) → lint Error**; WARN aggregation (count + capped samples, single diagnostic per rule/column); **`@pii` column samples masked in diagnostics and alert payloads**.
- **Parser**: `ON FAILURE` clauses (single/multiple/`TO`); `ASSERT JOB` grammar; `UNIQUE_FIRST` without `BY` → `SyntaxException`.
- **Linter**: malformed `@expect`; `@fail: 'QUARANTINE'` on nested SELECT; QUARANTINE without target; **quarantining statement without an enclosing section label**; **orphaned `ON FAILURE` clause with zero matching rules (symmetric check)** — all Errors; `#temp` quarantine target → Info.
- **Engine**: THROW/WARN/QUARANTINE end-to-end incl. `__dq_*` columns, projected-value semantics, and **pre-projection input-row capture** (quarantine row carries input columns absent from the projection); UNIQUE over a dataset above the spill threshold (deterministic, single source read verified with a read-counting fake source); zero-rules ⇒ no extra pass; pushdown-pin regression test.
- **Metrics**: collector tallies (rows/nulls/quarantined/warned) incl. write-only sink; multi-sink `NULL_PERCENT` ambiguity error; persisted DQ metrics round-trip on SQLite **and** PostgreSQL history stores (additive migration).
- **Connector**: webhook sink vs a mock HTTP endpoint; payload per FORMAT; SECRET redaction in logs/errors; **egress-denied host and denied redirect hop are rejected**.
- **Job**: HISTORICAL math with seeded `JobHistory`; **cold-start skip-with-warning below `MinHistoryRuns`**; `QUARANTINE_PERCENT` end-to-end; ALERT fires; `ON CRITICAL_FAILURE THROW`; clean error when no metrics provider.
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
- **Single-pass batching of multiple UNIQUE columns** (optimization).
- **Deep Governance-dashboard findings integration** (a follow-on that reuses the lineage/stewardship read side; the persisted per-run DQ metrics from §6 are its designed feed).

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

3. **The UNIQUE duplicate-key map is in-memory.** §4 specified building it through
   `ExternalAggregateEngine` for spill-awareness. The row *stream* is spilled exactly once as
   designed (the source is never read twice — the correctness requirement), but the key→group map
   is a dictionary bounded by distinct key count. This is fine for the dimension-scale keys UNIQUE
   is normally declared on and wrong for very high-cardinality keys on a memory-constrained host.
   The spill-aware build is a v2 item.

4. **Retention pruning applies only to engine-managed targets.** `WITH (RETENTION = …)` prunes
   in-memory/`#temp` targets at end of run. For durable targets the engine logs that pruning is the
   target's own responsibility rather than issuing DELETEs against a user table it does not own.
   Connector-side retention is a v2 item.

Two smaller notes: `WARN_PERCENT` was added as a fourth `ASSERT JOB` metric (not in the original
predicate list) because the warn tally was already collected and asserting on it is the natural
companion to `QUARANTINE_PERCENT`; and the local-path pin covers the SQL pushdown paths as well as
the columnar ones, since remote pushdown bypasses local projection just as thoroughly.
