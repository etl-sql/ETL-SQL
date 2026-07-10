# Execution Transparency and Fallback Coverage (v0.15.0 Phase 5) — Design

**Status:** Implementation in progress; Slice A telemetry contract is implemented, with Slice B
columnar instrumentation and Slice C SQL pushdown instrumentation partially implemented.
**TODO items covered:** v0.15.0 Phase 5 (fallback reason telemetry, fallback frequency/cost
ranking, differential correctness and crossover benchmarks for new native paths).
**Completion gate:** users and maintainers can see why a query left a native/columnar path, how
often it happens, and whether adding a native path is justified.

---

## 1. Goal

The engine has several optimized paths: SQL pushdown, streaming execution, native columnar
projection/aggregation/join/sort, external spill operators, and row-engine fallback. Today those
decisions are spread across planners and handlers. A query can silently leave a fast path because
of one expression, type, collation, memory-admission result, or semantic guard.

Phase 5 creates a consistent **plan decision telemetry contract**. Every planner that accepts or
rejects a fast path emits a structured decision with a reason code, cost context, and fallback
destination. `EXPLAIN`, `EXPLAIN ANALYZE`, profile metrics, and certification reports can then
rank fallback frequency and cost.

### Non-goals

- **No native-path expansion without measurements.** This phase first makes fallbacks visible.
- **No correctness shortcuts.** Row-engine fallback remains the correctness baseline.
- **No noisy per-row telemetry.** Decisions are emitted per operator/plan attempt, not per row.
- **No user-facing stack traces or provider internals.** Reasons are stable engine categories.

---

## 2. Decision Event Model

Add a lightweight immutable event record in Core or Engine, depending on final ownership:

```csharp
public sealed record PlanDecision(
    string QueryId,
    string OperatorId,
    string CandidatePath,
    PlanDecisionOutcome Outcome,
    string ReasonCode,
    string Message,
    IReadOnlyDictionary<string, string> Attributes);
```

| Field | Purpose |
| :--- | :--- |
| `QueryId` | Correlates decisions within one statement |
| `OperatorId` | Stable plan node id or synthetic path id |
| `CandidatePath` | `SqlPushdown`, `ColumnarAggregate`, `ColumnarJoin`, `ExternalSort`, etc. |
| `Outcome` | `Accepted`, `Rejected`, `Fallback`, `Degraded` |
| `ReasonCode` | Stable machine-readable category |
| `Message` | Short human-readable explanation |
| `Attributes` | Low-cardinality facts: type name, function name, estimate, threshold |

Telemetry lives on `ExecutionTelemetryManager` as a bounded list or ring buffer. The default cap
should be high enough for a query plan but low enough to protect long scripts from unbounded growth.

---

## 3. Reason Taxonomy

Reason codes are stable and documented. New codes require tests.

| Code | Meaning | Examples |
| :--- | :--- | :--- |
| `UnsupportedExpression` | Planner cannot evaluate an expression natively | UDF, complex CASE, unsupported scalar function |
| `UnsupportedType` | Column type has no native implementation | JSON object, mixed dynamic value |
| `UnsupportedCollation` | Required string comparison semantics differ | Case-sensitive or culture-specific sort |
| `SemanticGuard` | Native path would change semantics | Lateral alias dependency, duplicate-preserving join rule |
| `MemoryAdmissionRejected` | Estimate exceeds native path memory contract | Oversized join build side |
| `MissingStatistics` | Required row count/width estimate unavailable | Non-replayable source without estimate |
| `NonReplayableSource` | Planner probe would consume data irreversibly | Streaming-only connector |
| `ConnectorCapabilityMissing` | Remote connector cannot push down required syntax | Dialect keyword/function gap |
| `GovernanceCeiling` | Policy/config ceiling prevents the candidate | Max parallel degree or spill ceiling |
| `PlannerException` | Sanitized unexpected planner failure, followed by safe fallback | Defensive catch path |

Messages should include enough detail to act on without leaking secrets. For example, name the
unsupported function, but do not include connection strings or raw provider errors.

---

## 4. Surfaces

| Surface | Required behavior |
| :--- | :--- |
| `EXPLAIN` | Adds accepted/rejected path notes per plan row where decisions are known statically |
| `EXPLAIN ANALYZE` | Adds actual fallback decisions and cost attribution |
| Profile metrics | Includes counts by `ReasonCode` and `CandidatePath` |
| Scale reports | Include fallback counts for certified scenarios; unexpected fallback is failure where native path is required |
| Logs | Debug-level structured events only; no console writes from engine internals |

`EXPLAIN` output should stay readable. Detailed attributes can go to `INTO #plan` rows or JSON
profile output; terminal rendering can show a compact reason summary.

---

## 5. Ranking Fallbacks

Phase 5 adds a report that ranks fallback opportunities by frequency and cost:

| Metric | Source |
| :--- | :--- |
| Count by reason/path | `PlanDecision` events |
| Rows affected | Plan node estimated/actual rows |
| Elapsed cost | `EXPLAIN ANALYZE` node duration where available |
| Spill cost | `TotalSpilledBytes` delta where attributable |
| Memory impact | peak working set or grant rejection data |

The output should identify high-value work in this form:

```text
ColumnarAggregate rejected 42 times by UnsupportedExpression(REGEX_MATCH);
median affected rows 1.2M; estimated row-engine cost 8.4s.
```

That ranking becomes the input to future native-path work. A new native path is only justified
when a representative workload shows meaningful frequency or cost.

---

## 6. New Native Path Admission

Before adding a native path, require:

- A ranked fallback report showing measurable value.
- Differential correctness tests against the row engine at small and medium scale.
- Crossover benchmarks showing the native path does not regress small/medium workloads.
- A documented semantic envelope and fallback reason for excluded cases.
- Certification or lower-tier scale coverage if the path touches large-data claims.

Native path tests must include a forced fallback comparison. The fallback is not dead code; it is
the correctness baseline and the recovery path for unsupported shapes.

---

## 7. Delivery Plan

1. **Slice A — telemetry contract.** Add `PlanDecision` storage, reason taxonomy, cap/clear
   behavior, and unit tests. *(Implemented: `PlanDecision`, `PlanDecisionOutcome`, stable
   `PlanDecisionReasonCodes`, bounded sanitized storage on `ITelemetryContext`, and
   `PlanDecisionTelemetryTests`.)*
2. **Slice B — columnar planner instrumentation.** Instrument aggregate, join, sort, and projection
   native planners with accepted/rejected decisions. *(In progress: `SelectStatementHandler`
   records accepted/fallback decisions for columnar join, sort, grouped aggregate, global
   aggregate, projection/filter, and columnar `SELECT INTO` routes. Planner-specific rejection
   details still need richer result objects where candidate open currently returns only null.)*
3. **Slice C — pushdown and external engine instrumentation.** Emit decisions for SQL pushdown,
   streaming vs blocking, spill admission, and memory rejection. *(In progress: SQL `SELECT`
   pushdown now records accepted/fallback `SqlPushdown` decisions for standard result streaming
   and `SELECT INTO`, including connection and fallback destination attributes. External sort,
   join, aggregate, and window engines record accepted decisions. External join and aggregate
   memory-governor pressure records `MemoryAdmissionRejected` degraded/rejected decisions for
   repartition, spill-only churn, or fail-fast destinations. The row pipeline records
   streaming-vs-blocking decisions for direct join projection, Top-N heap, sort/window prefix
   probes, and aggregate/window spill handoff. Deeper spill admission detail and per-operator cost
   attribution remain open.)*
4. **Slice D — surfaces.** Extend `EXPLAIN`, `EXPLAIN ANALYZE`, profile metrics, and cert reports.
   *(In progress: static `EXPLAIN` includes `Plan Candidates` and `Plan Notes` for obvious
   native-path candidates and runtime gates. `SHOW PROFILE` includes plan-decision totals and
   grouped fallback summaries. `EXPLAIN ANALYZE` appends plan-decision totals and fallback summary
   columns after executing the query. Gate F native-required evidence records plan-decision counts
   and fails validation on unexpected fallback.)*
5. **Slice E — ranking report.** Add a script or report artifact that summarizes fallback
   frequency/cost across representative workloads. *(In progress:
   `scripts/Summarize-PlanFallbacks.ps1` aggregates existing JSON evidence/profile fallback
   summaries by candidate path and reason code, carries same-object elapsed/spill/row/peak-memory
   cost context when present, then emits ranked JSON/Markdown outputs. Representative workload
   capture and per-operator cost attribution remain open.)*
6. **Slice F — native admission harness.** Add differential correctness requirements and crossover
   benchmarks for candidate native paths. *(In progress: `ColumnarCrossoverBenchmarks` compares
   row-reference and native columnar implementations for filter/projection, grouped aggregate, sort,
   and inner join at small/medium sizes. Published benchmark captures and admission thresholds
   remain open.)*

---

## 8. Test Plan

| Test | Proves |
| :--- | :--- |
| Reason taxonomy tests | Codes are stable and messages are sanitized |
| Planner unit tests | Each known rejection emits the expected reason |
| Explain tests | `EXPLAIN INTO #plan` includes decisions without breaking existing columns |
| Certification route tests | Scenarios requiring native path fail on unexpected fallback |
| Ranking fixture tests | Summary groups by reason/path and orders by cost |
| Allocation tests | Decision collection does not create per-row churn |

---

## 9. Completion Criteria

- A maintainer can answer "why did this query leave the native path?" from `EXPLAIN ANALYZE`.
- Certification reports show unexpected fallback as a failure for native-required scenarios.
- A representative fallback ranking exists before any Phase 5 native-path expansion is approved.
