# ETL-SQL Product Roadmap

This document tracks future product tracks and candidate phases. When development begins, the next
actionable phase is moved to `TODO.md`. Shipped work belongs in `CHANGELOG.md` and the release notes
under `docs/releases/`.

The enterprise operating model, authority hierarchy, trust boundaries, and progressive deployment
promise are defined in
[`docs/architecture/roadmaps/Enterprise_Platform_Strategy.md`](docs/architecture/roadmaps/Enterprise_Platform_Strategy.md).

---

## Future Candidate Phases

### Data Quality v2

v1 (column `@expect`/`@fail` rules, quarantine/warn capture, `ASSERT JOB`, the `WEBHOOK` connector)
shipped in v0.17.0. The v2 metric-depth slice is implemented; remaining v2 work is tracked here.
The full design, including the as-built deviations v1 left behind, lives in
[`docs/architecture/decisions/DataQualityRules.md`](docs/architecture/decisions/DataQualityRules.md).

Recommended order (rationale in the design doc's "v2 sequencing" section):

1. **Metric depth** — shipped: `NULL_PERCENT ... OF HISTORICAL` (with a per-column run-metrics table),
   qualified `NULL_PERCENT(target.col)`, a `FRESHNESS(col) < interval` predicate, and
   `WITHIN n SIGMA OF HISTORICAL`.
2. **Alert quality** — transition-based alerting and recovery notifications, so a nightly-failing
   job cannot train people to mute the channel.
3. **Quarantine remediation** — the headline promise: disposition model, `REPLAY QUARANTINE`,
   orchestrator manifest, replay lease, Portal steward grid. Largest slice. **Promote to first if
   user feedback shows quarantine tables accumulating** — v1 ships capture with no workflow.
4. **Scale hardening** — spill-aware UNIQUE key map, single-pass UNIQUE batching, connector-side
   retention. Demand-triggered; each has a recorded trigger in the design doc.
5. **Governance dashboard integration** — consumes the output of the slices above.

v3 direction (join-statement replay via probe-side provenance) and nested-script replay are recorded
in the same document as directions only.
