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
- [x] Remove avoidable row, batch, Arrow-builder, and serialization object churn through pooled or
  reusable buffers with explicit ownership and deterministic release. *(feat/spill-churn-reduction,
  three slices, each traced to a line of the published baseline profile: schema-backed Arrow spill
  rehydration + snapshot plans (was per-row dynamic dictionaries + metadata-string interpolation),
  shared expanded qualification schema in StreamingQueryEngine (was per-row Clone + Columns dict +
  interpolated "from.col" dynamic entries — the single largest line at ~40%), allocation-free
  per-row constraint checks (delegate/enumerator, entry-allocated error closure, occurrence
  dictionary in SetSchema via same-layout fast path), hoisted per-row Concat wrapper, and lazy
  validation-error list. 10M-row result vs baseline: 495.5k→863.2k rows/s (+74%), 16.3→6.1 GB
  allocated (1,708→638 B/row, −63%), GC pause 7.4→3.7 s (−50%), CPU −39%; correctness/spill/I-O
  identical; full standard suite green. Remaining top allocations are the row representation
  itself (Object[]/Row/boxed values) — native-path scope (Phase 5), not avoidable churn; one
  residual ~5% closure (DisplayClass57_0) documented in the profile reports.)*
- [x] Add allocation and GC regression budgets at 10M/50M plus the operator-run 1B certification;
  throughput improvements do not pass if peak memory containment or correctness regresses.
  *(feat/alloc-regression-budgets: `Compare-AllocBudget.ps1` fails on bytes/row (+10%), GC gen2
  (+30%/+5), GC pause (+35%/+500 ms), and peak-working-set containment (+15%) vs blessed,
  machine-pinned budgets in `certification-results/spill-alloc-budgets/` (10M: 638 B/row, gen2 81,
  peak 238 MB; 50M captured alongside). `Test-SpillAllocProfile.ps1` auto-compares every run
  (`-UpdateBudget` blesses); Gate F gains a resumable `AllocProfile` scenario so the operator-run
  1B cert captures the allocation profile, while the budget comparator warns and skips if the
  checked-in 1B budget has not been established yet; `Test-PreRelease` enforces the 10M budget under
  `-IncludeStandardScale`. Verified green path, tamper-fail (all three inflated metrics reported,
  exit 1), and gate-plan inclusion.)*
- [x] Follow-up: close the 1B allocation-budget evidence gap by revising the Phase 1 completion note
  to state the current behavior truthfully. The checked-in budgets cover 10M and 50M rows; a real
  `certification-results/spill-alloc-budgets/budget-1000000000rows.json` should only be added after
  a known-good 1B `AllocProfile` certification run is actually captured and blessed.
- [x] Follow-up: clarify or fix `scripts/Compare-AllocBudget.ps1` PowerShell compatibility. The
  comparator was made Windows PowerShell-safe by replacing Unicode dash text and isolating the
  re-blessing hint string instead of relying on one long interpolated message.

### Phase 2: Adaptive resource utilization

- [x] Add a bounded resource controller that can adjust batch size, worker count, prefetch depth,
  spill concurrency, and operator grant requests from measured CPU, memory pressure, queue depth, and
  storage latency.
  *(Slice A progress: observe-mode Core implementation added with `AdaptiveExecutionController`,
  per-job `AdaptiveAdvisor`, `ResourceSignalSampler`, bounded setpoints, decision log, cooldown,
  and active-advisor fairness. Slice B progress: execution contexts now expose effective adaptive
  setpoints and `PARALLEL`/`PARALLEL FOR` admission consumes the advised worker ceiling when adaptive
  mode is explicitly enabled; batch-size consumers and operator memory-grant consumers now read
  effective setpoints while the `SET` statements remain static ceilings; root evaluators now run a
  bounded resource sampler loop while adaptive mode is enabled; spill writer calls are gated by the
  effective spill-write concurrency setpoint without changing writer lifetime or format capability.
  Temp-table spill pipeline depth now supports the bounded `0`/`1` behavior: `0` forces synchronous
  spill writes, `1` preserves the existing one-write overlap. Deeper multi-write pipelining remains
  Slice C scope because it requires independent extent ownership per in-flight write. Runtime sampling
  now feeds CPU, memory, grant pressure, spill-write queue depth, and measured spill-write latency.)*
- [x] Define stable hysteresis, minimum/maximum bounds, fairness across concurrent jobs, and explicit
  configuration overrides; adaptation must not oscillate or exceed governance policy.
  *(Slice A progress: pure unit coverage proves high-pressure scale-down, idle slow-ramp,
  deadband, cooldown, floors/ceilings, and two-advisor worker/grant fairness. Slice B coverage proves
  evaluator config enables bounded effective setpoints while static `SET` ceilings remain intact.)*
- [x] Preserve deterministic single-worker execution for debugging/certification and prove that
  adaptive mode scales down under pressure as well as up when capacity is idle.
  *(Coverage proves idle scale-up, high CPU scale-down, spill-latency scale-down, and a worker ceiling
  of one remains one even under idle-capacity samples.)*

### Phase 3: Performance regression quality

Design: [PerformanceRegressionQuality.md](Docs/Design/PerformanceRegressionQuality.md)

- [x] Replace Gate F's catastrophe-only throughput floor with scenario-specific warning and failure
  bands derived from checked-in baselines, while retaining a portable absolute safety floor for
  slower supported hardware.
  *(Complete: `Compare-CertBaseline.ps1` supports scenario-family and per-baseline
  warn/fail bands, schema v1 flat metrics and schema v2 metric objects, separate warning/failure
  reporting, Markdown output, missing-baseline warnings, hardware-mismatch suppression of
  performance failures, and explicit `-RegressionPct` legacy override. Checked-in smoke/standard
  baselines now carry per-scenario bands and sample policies; Gate F evidence validation can compare
  operator-run reports against a supplied baseline.)*
- [x] Record runtime, hardware, configuration, commit, and variance across repeated samples; reject
  statistically meaningful regressions rather than relying on one unusually fast or slow run.
  *(Complete: `Test-ScaleCertification.ps1` and `Test-ScaleBaseline.ps1` emit
  schema-versioned reports with commit metadata, source fingerprint, config fingerprint, `host`
  metadata alongside the legacy `hardware` alias, and Markdown evidence lines. Scale certification
  supports repeated `-Samples`, aggregates scenario medians/maxima with distribution fields and raw
  sample metrics, baseline capture defaults to five samples, and the comparator warns when a run has
  fewer samples than baseline policy requests.)*
- [x] Keep Gate F operator-run and outside smoke/release lanes, but require a current-commit run before
  publishing performance claims or closing a release candidate that changes certified paths.
  *(Complete: `Test-GateFEvidence.ps1` enforces that captured Gate F evidence passed, contains the
  required scenario set, reports source/config metadata warnings, and belongs to the current commit
  or explicit `-RequiredCommit`; pre-release lanes record smoke/standard comparator Markdown
  artifacts while Gate F remains an operator-run claim gate.)*

### Phase 4: Extend operator-specific billion-row coverage

Design: [BillionRowOperatorCertification.md](Docs/Design/BillionRowOperatorCertification.md)

- [x] Define separate admission and success criteria for external equi-join and sort at 1B, including
  skew, partition passes, extent counts, spill bytes, useful throughput, and required free disk.
  *(Slice A complete: `certification-results/billion-row-operator-scenarios.json` defines the
  operator matrix and per-scenario contracts, including external sort and external equi-join
  admission, telemetry, success criteria, resume keys, and non-goals. `Test-GateF.ps1` now emits
  `scenarioManifests` and `admission` sections in future Gate F reports, and
  `BillionRowOperatorManifestTests` validates the manifest contract. Slice B progress:
  `Test-GateF.ps1 -Scenario ExternalSort` runs the explicit external-sort candidate with generated
  rows, multi-key streaming order validation, resume/reuse keys, disk admission, and report output;
  `Test-GateF.ps1 -Scenario ExternalJoin` runs the explicit external equi-join candidate with
  generated left/right streams, controlled overlap, mathematical result-count/checksum validation,
  partition-pass telemetry, resume/reuse keys, disk admission, and report output. The matrix stays
  Candidate until real 1B operator-run artifacts pass.)*
- [x] Add bounded 1B scenarios incrementally for high-cardinality grouping, eligible window shapes,
  holistic aggregates, and heterogeneous `MERGE`; a fail-fast memory contract is not equivalent to
  spill-to-completion certification.
  *(Complete for v0.15.0 Phase 4 scope: `Test-GateF.ps1 -Scenario HighCardinalityGrouping` runs an explicit
  high-cardinality external aggregate candidate with generated groups, `COUNT`/`SUM`/`MIN`/`MAX`,
  formula-based validation, spill/partition telemetry, resume/reuse keys, disk admission, and report
  output. `Test-GateF.ps1 -Scenario EligibleWindowRowNumber` runs an explicit bounded
  `ROW_NUMBER` external-window candidate with deterministic partitions, per-partition sequence
  validation, spill/partition telemetry, resume/reuse keys, disk admission, and report output.
  Heterogeneous `MERGE` remains manifest-only and Not certified pending bounded source/target
  staging evidence; holistic aggregates remain Not certified pending a bounded exact or approximate
  design. These non-certified states are deliberate matrix outcomes, not 1B claims.)*
- [x] Publish the exact certified matrix and keep unsupported expressions, adversarial distributions,
  and row-engine fallbacks explicit. Do not introduce a blanket “all SQL at 1B” claim.
  *(Complete: `Docs/Large_Data_Certification.md` publishes the manifest-backed billion-row operator
  matrix with Certified/Candidate/Not certified states, pending artifacts, and non-claim language.
  `BillionRowOperatorManifestTests` now verifies the public matrix matches
  `certification-results/billion-row-operator-scenarios.json` and rejects broad "1B SQL support"
  wording.)*

### Phase 5: Fallback coverage and execution transparency

Design: [ExecutionTransparencyAndFallbacks.md](Docs/Design/ExecutionTransparencyAndFallbacks.md)

- [ ] Emit plan/telemetry reasons whenever a query leaves a native columnar path, including the
  unsupported expression, type/coercion, collation, memory-admission, or semantic constraint.
  *(Slice A progress: added the immutable `PlanDecision` contract, stable reason-code taxonomy,
  bounded sanitized storage on `ITelemetryContext`, clear/cap behavior, and focused tests. Planner
  instrumentation for native columnar rejection/acceptance points remains next. Slice B progress:
  `SelectStatementHandler` now records accepted/fallback decisions for native columnar join, sort,
  grouped aggregate, global aggregate, projection/filter, and columnar `SELECT INTO` routes, with
  focused routing tests covering accepted aggregate/projection paths and expression fallback.
  Slice C progress: SQL `SELECT` pushdown now emits accepted/fallback `SqlPushdown` decisions for
  standard result streaming and `SELECT INTO`, including connection and row-engine fallback
  attributes; focused pushdown tests cover accepted remote execution and engine-only-function
  fallback. External sort, join, aggregate, and window engines now emit accepted plan decisions,
  and external join/aggregate memory-governor pressure records `MemoryAdmissionRejected`
  degraded/rejected decisions for repartition, spill-only churn, or fail-fast destinations. The
  row pipeline now records streaming-vs-blocking decisions for direct join projection, Top-N heap,
  sort/window prefix probes, and aggregate/window spill handoff.
  Slice D progress: `SHOW PROFILE` now includes plan-decision totals and a grouped
  `CandidatePath:ReasonCode=count` fallback summary for the current telemetry window;
  `EXPLAIN ANALYZE` now appends plan-decision totals and fallback summary columns to the analyzed
  plan output. Certification progress: the native-required Gate F columnar-core metric records
  plan-decision counts and fails on fallback, and `Test-GateFEvidence.ps1` fails current evidence
  when native-required scenarios report fallback decisions. Static `EXPLAIN` now includes
  `Plan Candidates` and `Plan Notes` columns that identify obvious native-path candidates and the
  runtime gates that decide acceptance. Scale-certification metrics and Gate F operator metrics now
  include plan-decision counts plus fallback/degraded/rejected summaries so ranking can consume
  checked-in or operator-run evidence JSON directly.)*
- [ ] Rank fallback frequency and cost from representative workloads, then add native paths only where
  measurements justify them; retain the row engine as the correctness fallback.
  *(Slice E progress: `scripts/Summarize-PlanFallbacks.ps1` aggregates fallback summaries from JSON
  evidence/profile artifacts, ranks them by `CandidatePath`/`ReasonCode` frequency, carries
  same-object elapsed/spill/row/peak-memory cost context when present, and can emit JSON plus
  Markdown reports. Scale-certification and Gate F metric JSON now emit those summary fields for
  representative evidence capture. Checked-in representative workload captures and per-operator
  cost attribution remain open before approving any new native-path expansion.)*
- [ ] Add differential correctness and crossover benchmarks for every new native path so small and
  medium workloads do not regress for the large-tier headline.
  *(Harness progress: `ColumnarCrossoverBenchmarks` now provides explicit row-reference versus
  native columnar comparisons for filter/projection, grouped aggregate, sort, and inner join at
  1k/50k rows. Admission thresholds are checked in as
  `certification-results/columnar-crossover-admission.json` and validated by
  `ColumnarCrossoverAdmissionTests`: exact checksum parity, no more than 10% small-workload
  slowdown, no medium-workload slowdown, no medium-workload allocation increase, and at least five
  samples. Per-new-path differential requirements are checked in as
  `certification-results/native-path-differential-requirements.json` and validated by
  `NativePathDifferentialRequirementsTests`. Checked-in benchmark result captures remain open.)*

### Phase 6: Concurrent, PostgreSQL, and failure soak certification

Design: [ConcurrentPostgresFailureSoak.md](Docs/Design/ConcurrentPostgresFailureSoak.md)

- [ ] Run sustained PostgreSQL-backed Portal/Orchestrator load at representative report/job/history
  counts and concurrent execution levels; measure pool saturation, query latency, scheduler fairness,
  lease behavior, and database growth rather than inferring HA performance from SQLite tests.
- [ ] Add multi-hour concurrent large-job soaks covering mixed scan, spill, join, and sort workloads
  under shared memory and disk budgets, including cancellation at each spill phase.
- [ ] Inject disk-full/low-space, slow disk, corrupt or incomplete extent, process crash, restart,
  orphan cleanup, and temp-root exhaustion; verify bounded recovery with no leaked grants, handles,
  extents, or silently duplicated/lost mutations.

### Phase 7: SME Secret Management & Administration Hardening

Design: [SMESecretManagementAdministrationHardening.md](Docs/Design/SMESecretManagementAdministrationHardening.md)

- [ ] Design and implement the administrative "middle ground" for connection secret management,
  supporting SMEs without external vaults while maintaining Zero-Trust boundaries. The built-in
  `OsSecretStore`, environment-provider, and Portal-managed encrypted store are the supported
  low-dependency paths; HTTPS vault integration remains optional for organizations that already
  operate one.
- [ ] Add an administrative CLI command (`etl-sql admin set-secret --name <name> --value <value>`)
  to securely encrypt and write secrets to the local OS Secret Store (`OsSecretStore`) under the
  machine/root context, ensuring the Portal process remains restricted to read-only.
- [ ] Implement a Portal-managed, database-backed encrypted secret store where credentials can be
  entered via Web UI and are stored encrypted at rest using the portal's cluster-wide keys, solving
  the multi-node sync problem for simple HA environments without requiring a separate vault product.
- [ ] Add named-secret syntax parity and tests so `SECRET:name` can be used consistently wherever
  `ENC:...` credential values are accepted, or document quoted `'SECRET:name'` and quoted
  `'ENC:...'` as the canonical forms if unquoted secret-reference literals are not added.
- [ ] Extend named-secret resolution and redaction beyond password-like fields through connector
  metadata or governance policy. Organizations must be able to mark `HOST`, `SERVER`, `DATABASE`,
  `PATH`, `ROOT_PATH`, bucket/container names, endpoints, and similar connection metadata as
  sensitive without making those fields globally secret for every deployment.
- [ ] Design a Portal Connection Catalog to store connection metadata (`HOST`, `PORT`, `DATABASE`,
  `USER`, default options) centrally using `SECRET:name` credential references. Developers should
  be able to query approved pre-configured connections without re-declaring endpoints or exposing
  credentials in scripts.
- [ ] Add catalog governance for pre-configured connections: ownership, environment/tenant scope,
  per-connection RBAC, approval/audit on create/update/delete, last-used/impact inventory, masked
  preview/test-connection diagnostics, and runtime expansion that never persists resolved secrets
  or sensitive metadata back into scripts, logs, reports, lineage, or cached execution state.
- [ ] Add secret and connection lifecycle operations for SME deployments: rotate, disable, delete,
  verify, rebind, export/import metadata without secret material, backup/restore validation, HA key
  compatibility checks, and clear behavior when the OS store, Portal store, or configured provider
  is unavailable.
- [ ] Move administrative operations (capacity reporting, failure digests, and backup reporting
  currently in `samples/admin_operations`) into first-class background services managed natively in
  Portal/Orchestrator configuration, removing the need for user-maintained scheduler scripts.
- [ ] Give native admin background services production controls: enable/disable, schedule, HA
  singleton lease, retry/backoff, retention, audit trail, notification targets through configured
  SMTP/Portal channels, and safe migration from the current sample scripts.

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
  *(Current caveat: the checked-in `certification-results/gate-f-1b/gate-f-report.json` predates the
  `AllocProfile` scenario and schema v2 source/config fingerprints. Before publishing Gate F
  performance claims or closing a release candidate that changes certified paths, rerun Gate F for
  the current commit and validate it with `Test-GateFEvidence.ps1 -RequiredScenario All`.)*
- [ ] Adaptive execution demonstrates higher utilization when resources are idle and safe throttling
  under contention, with fairness and governance ceilings proven by automated tests.
- [ ] Every newly advertised 1B operator has an isolated, resumable certification scenario and an
  explicit non-goal list; operators that miss their criteria remain unadvertised.
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.
