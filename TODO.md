# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## v0.15.0 — Adaptive Execution & Extended Large-Data Certification

The v0.14.0 billion-row program established a credible but deliberately narrow certification for
streaming scan, filter, projection, low-cardinality aggregation, and spill-backed `#temp` staging.
v0.15.0 improves efficiency and concurrency without weakening bounded-memory behavior or turning
that result into an unsupported blanket billion-row claim.

> **Branch model:** integration branch is `release/v0.15.0`. Create one feature branch per item or
> phase off `release/v0.15.0` (e.g. `feat/spill-alloc-profiling`), merge back into
> `release/v0.15.0` when the feature's tests are green, and merge `release/v0.15.0` → `main` only
> at release. `main` stays at the last shipped release (v0.14.0).
>
> **Deferred to roadmap:** the enterprise-security continuation (deferred Phase 3 hardening,
> Phase 4 Central Security Events, Phase 5 Certification & Operations) remains in `ROADMAP.md`;
> promote it only if it joins this release's scope.

### Phase 1: Spill allocation and GC efficiency

- [x] Profile the Gate F `#temp` round trip by allocation type and call site; publish retained bytes,
  cumulative allocation, allocation rate, GC counts/pause, CPU time, and physical I/O before changing
  the implementation. *(Harness: `SpillAllocationProfilingTests` + `AllocationTypeProfiler`
  (GCAllocationTick by-type sampling) + `ProcessIoCounters`, operator entry
  `scripts/Test-SpillAllocProfile.ps1`; call-site stacks via `dotnet-trace --profile gc-verbose`.
  Published 10M baseline `certification-results/spill-alloc-profile/`: 495k rows/s, 16.3 GB
  allocated (1,708 B/row) vs 252 MB spill, GC 944/449/103 with 7.4 s pause of a 20.2 s run,
  retained delta 6.2 MB — churn, not leaks. Top churn: Dictionary<string,object>/Entry[]/Object[]/
  Row (~50% is row-shape overhead), plus per-row `Func<TableConstraintInfo,bool>` +
  DisplayClass closures from `DataTable.AddRowAsync` (`DataModel.cs:516`) — the target list for
  the pooled-buffer item below.)*
- [ ] Remove avoidable row, batch, Arrow-builder, and serialization object churn through pooled or
  reusable buffers with explicit ownership and deterministic release.
- [ ] Add allocation and GC regression budgets at 10M/50M plus the operator-run 1B certification;
  throughput improvements do not pass if peak memory containment or correctness regresses.

### Phase 2: Adaptive resource utilization

- [ ] Add a bounded resource controller that can adjust batch size, worker count, prefetch depth,
  spill concurrency, and operator grant requests from measured CPU, memory pressure, queue depth, and
  storage latency.
- [ ] Define stable hysteresis, minimum/maximum bounds, fairness across concurrent jobs, and explicit
  configuration overrides; adaptation must not oscillate or exceed governance policy.
- [ ] Preserve deterministic single-worker execution for debugging/certification and prove that
  adaptive mode scales down under pressure as well as up when capacity is idle.

### Phase 3: Performance regression quality

- [ ] Replace Gate F's catastrophe-only throughput floor with scenario-specific warning and failure
  bands derived from checked-in baselines, while retaining a portable absolute safety floor for
  slower supported hardware.
- [ ] Record runtime, hardware, configuration, commit, and variance across repeated samples; reject
  statistically meaningful regressions rather than relying on one unusually fast or slow run.
- [ ] Keep Gate F operator-run and outside smoke/release lanes, but require a current-commit run before
  publishing performance claims or closing a release candidate that changes certified paths.

### Phase 4: Extend operator-specific billion-row coverage

- [ ] Define separate admission and success criteria for external equi-join and sort at 1B, including
  skew, partition passes, extent counts, spill bytes, useful throughput, and required free disk.
- [ ] Add bounded 1B scenarios incrementally for high-cardinality grouping, eligible window shapes,
  holistic aggregates, and heterogeneous `MERGE`; a fail-fast memory contract is not equivalent to
  spill-to-completion certification.
- [ ] Publish the exact certified matrix and keep unsupported expressions, adversarial distributions,
  and row-engine fallbacks explicit. Do not introduce a blanket “all SQL at 1B” claim.

### Phase 5: Fallback coverage and execution transparency

- [ ] Emit plan/telemetry reasons whenever a query leaves a native columnar path, including the
  unsupported expression, type/coercion, collation, memory-admission, or semantic constraint.
- [ ] Rank fallback frequency and cost from representative workloads, then add native paths only where
  measurements justify them; retain the row engine as the correctness fallback.
- [ ] Add differential correctness and crossover benchmarks for every new native path so small and
  medium workloads do not regress for the large-tier headline.

### Phase 6: Concurrent, PostgreSQL, and failure soak certification

- [ ] Run sustained PostgreSQL-backed Portal/Orchestrator load at representative report/job/history
  counts and concurrent execution levels; measure pool saturation, query latency, scheduler fairness,
  lease behavior, and database growth rather than inferring HA performance from SQLite tests.
- [ ] Add multi-hour concurrent large-job soaks covering mixed scan, spill, join, and sort workloads
  under shared memory and disk budgets, including cancellation at each spill phase.
- [ ] Inject disk-full/low-space, slow disk, corrupt or incomplete extent, process crash, restart,
  orphan cleanup, and temp-root exhaustion; verify bounded recovery with no leaked grants, handles,
  extents, or silently duplicated/lost mutations.

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
- [ ] Adaptive execution demonstrates higher utilization when resources are idle and safe throttling
  under contention, with fairness and governance ceilings proven by automated tests.
- [ ] Every newly advertised 1B operator has an isolated, resumable certification scenario and an
  explicit non-goal list; operators that miss their criteria remain unadvertised.
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.
