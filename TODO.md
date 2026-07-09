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
  1B cert checks a 1B budget; `Test-PreRelease` enforces the 10M budget under
  `-IncludeStandardScale`. Verified green path, tamper-fail (all three inflated metrics reported,
  exit 1), and gate-plan inclusion.)*

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
- [ ] Adaptive execution demonstrates higher utilization when resources are idle and safe throttling
  under contention, with fairness and governance ceilings proven by automated tests.
- [ ] Every newly advertised 1B operator has an isolated, resumable certification scenario and an
  explicit non-goal list; operators that miss their criteria remain unadvertised.
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.
