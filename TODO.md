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

### Phase 7: Shared Connection & Secret Governance follow-ups

Design: [SMESecretManagementAdministrationHardening.md](Docs/Design/SMESecretManagementAdministrationHardening.md)

These items continue the shipped SME secret-management work. The first priority is tightening who
may use shared connections and giving administrators impact visibility before they disable or
delete cataloged secrets/connections.

- [ ] Add per-connection use ACLs for cataloged shared connections. Enforce who may expand and use
  `SHARED:alias`, not only who may manage the catalog entry. Caller/service identity must flow into
  the engine expansion path, group-based grants should match the existing Portal ACL model, and
  denials must be audited without resolving or logging secrets.
- [ ] Add connection and secret impact inventory before disable/delete operations. Show reports,
  subscriptions, scheduled jobs, and scripts that reference a shared connection alias or secret name,
  using script inspection where possible, and record last-used details per consumer rather than only
  a single timestamp per entry.
- [ ] Add finer-grained sensitive-metadata classification. Extend the current organization-wide
  `Governance:Secrets:SensitiveConnectionFields` with per-connector defaults and per-catalog-entry
  field classification so endpoints, paths, bucket/container names, and similar metadata can be
  protected without making every deployment treat the same field as secret.
- [ ] Decide whether catalog approval workflow belongs in this phase or the broader Review Workflow
  track. If included here, design propose/approve for shared-connection create/update/delete with
  segregation of duties and audit; otherwise leave it in ROADMAP with the data-stewardship workflow.

### v0.15.0 completion gates

- [ ] Publish before/after Gate F allocation, GC, CPU, memory, I/O, and throughput results on the same
  hardware and workload; explain any tradeoff rather than selecting only favorable metrics.
  *(Current caveat: the checked-in `certification-results/gate-f-1b/gate-f-report.json` predates the
  `AllocProfile` scenario and schema v2 source/config fingerprints. Before publishing Gate F
  performance claims or closing a release candidate that changes certified paths, rerun Gate F for
  the current commit and validate it with `Test-GateFEvidence.ps1 -RequiredScenario All`.)*
- [ ] PostgreSQL sustained-load and concurrent failure-soak suites pass with documented capacity and
  recovery limits, and the normal small/medium regression lanes remain green.
