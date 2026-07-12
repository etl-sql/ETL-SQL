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
> **Deferred to roadmap:** Phase 4 Central Security Events and Phase 5 Certification & Operations
> remain in `ROADMAP.md`; promote them only if they join this release's scope.

### Enterprise Phase 3 hardening closeout

The main Phase 3 enterprise policy-authority and operation-boundary enforcement work is already
implemented. This closeout list tracks the remaining hardening and retained evidence needed before
calling the enterprise continuation fully current.

- [x] Complete handle-based or equivalent race-resistant `DELETE`, `MOVE`, and `RENAME`
  operations on supported platforms; add link/junction substitution tests at each mutation
  boundary.
- [x] Extend connect-time DNS re-pin, redirect re-authorization, and proxy-bypass controls beyond
  the REST connector to all policy-governed outbound HTTP/network clients, including SharePoint,
  Report Portal, Orchestrator, remote policy/vault access, discovery, and probe paths.
- [ ] Run and retain the deferred performance lane plus Windows and Linux enterprise certification
  evidence, including path/link races, DNS rebinding, redirects, connector aliases, and standalone
  behavior. *(Bundled into the Phase 6 operator runbook/evidence plan via
  `scripts/Test-EnterpriseHardeningCertification.ps1`; run once on Windows and once on Linux/WSL
  for the same run id.)*

### Phase 6: Concurrent, PostgreSQL, and failure soak certification

Design: [ConcurrentPostgresFailureSoak.md](Docs/Design/ConcurrentPostgresFailureSoak.md)

- [ ] Run sustained PostgreSQL-backed Portal/Orchestrator load at representative report/job/history
  counts and concurrent execution levels; measure pool saturation, query latency, scheduler fairness,
  lease behavior, and database growth rather than inferring HA performance from SQLite tests.
  *(CI-smoke evidence exists under `certification-results/postgres-ha-soak/ha-agent-20260711-01/`.
  Remaining work: run and publish representative/manual-certification capacity evidence with
  realistic report/job/history counts and documented capacity limits.)*
- [ ] Add multi-hour concurrent large-job soaks covering mixed scan, spill, join, and sort workloads
  under shared memory and disk budgets, including cancellation at each spill phase.
  *(CI-smoke evidence exists under `certification-results/ha-large-job-soak/ha-agent-20260711-01/`.
  Remaining work: run the `ManualCertification` large-job plan, publish the measured multi-hour
  artifacts, and document observed limits.)*
- [ ] Inject disk-full/low-space, slow disk, corrupt or incomplete extent, process crash, restart,
  orphan cleanup, and temp-root exhaustion; verify bounded recovery with no leaked grants, handles,
  extents, or silently duplicated/lost mutations.
  *(CI-smoke evidence exists under
  `certification-results/ha-fault-injection/ha-agent-20260711-01/`. Remaining work: run the
  destructive/manual fault suite against a live HA topology, publish recovery evidence, and document
  operational limits.)*

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
  *(Current caveat: the checked-in `certification-results/gate-f-1b/gate-f-report.json` predates the
  `AllocProfile` scenario and schema v2 source/config fingerprints. Before publishing Gate F
  performance claims or closing a release candidate that changes certified paths, rerun Gate F for
  the current commit and validate it with `Test-GateFEvidence.ps1 -RequiredScenario All`.)*
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.
