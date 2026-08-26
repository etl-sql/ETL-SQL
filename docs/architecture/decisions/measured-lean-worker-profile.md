# Measured lean worker profile decision

**Status:** Accepted — do not publish a dedicated worker artifact

**Decision date:** 2026-08-25

**Applies to:** v0.19.0

## Context

The sandbox worker currently uses the unified `ETL-SQL.App` executable. A separate engine-only host
could remove top-level CLI, TUI, Gateway, and administration dependencies, but the engine composition
root still requires the connector groups, reporting semantics, orchestration session support, and
governance services. Project-graph appearance alone does not show whether a second artifact pays for
its release, security, and certification cost.

The experiment therefore set its material-benefit threshold before comparing artifacts:

- At least 20% lower published size.
- At least 15% lower median cold-start latency or startup working set.
- No regression in container lifecycle or required execution contracts.
- A full certification matrix before any artifact may be published.

## Method

[`Measure-LeanWorkerProfile.ps1`](../../../scripts/Measure-LeanWorkerProfile.ps1) publishes the unified
CLI and the non-shipping fixture under `tools/lean-worker-experiment` with matched framework-dependent
settings. It records:

- Published bytes and the complete `.deps.json` dependency closure.
- Separate-process startup latency, current and peak working set, and loaded assemblies after the
  real DI composition root resolves an evaluator.
- Docker image bytes and create/start/exit/remove lifetime when `-MeasureSandbox` is selected.
- A configurable startup-only GB-second cost model. The model deliberately excludes steady-state
  execution and provider charges.

Every distribution discards one warm-up and retains at least three independent process samples. The
report records the commit, dirty-worktree state, runtime identifier, sample count, and exact assembly
and dependency inventories. Results are machine-specific and must be regenerated on a release host
before this decision is reconsidered.

## Results

The retained Windows x64 and Docker Desktop evidence is in
[`boundary-measurement.json`](../../../certification-results/lean-worker/boundary-measurement.json).

| Metric | Unified CLI | Engine-only fixture | Change |
| :--- | ---: | ---: | ---: |
| Published bytes | 207,872,025 | 205,686,326 | 1.05% smaller |
| Dependency closure | 158 | 156 | 2 fewer libraries |
| Loaded assemblies | 89 | 85 | 4 fewer assemblies |
| Median cold start | 289.511 ms | 294.003 ms | 1.55% slower |
| Median startup working set | 66,703,360 bytes | 67,887,104 bytes | 1.77% higher |
| Docker image bytes | 137,424,410 | 136,513,448 | 0.66% smaller |
| Median sandbox lifetime | 782.948 ms | 808.853 ms | 3.31% slower |
| Modeled monthly startup cost, 1M starts | $0.2998 | $0.3098 | $0.0101 higher |

The experiment removes the TUI and `System.CommandLine` from the direct closure, but shared engine
composition dominates the artifact and startup behavior. It misses both material-benefit thresholds
and regresses the two latency/cost measures.

## Trimming experiment

The opt-in self-contained partial-trim publish completed with the engine and report-handler
assemblies rooted for reflection. Its first post-DI startup probe failed because reflection-based
`System.Text.Json` serialization metadata was removed. The retained failure is in
[`trimmed-experiment.json`](../../../certification-results/lean-worker/trimmed-experiment.json).

This is a functional regression at the earliest gate. Connector discovery, execution,
cancellation, governance, and deployment-profile certification were not used to excuse that failure;
the experiment stopped and no trimmed artifact was promoted.

## Decision

Do not add a dedicated worker project to the product solution, do not switch the sandbox image, and
do not publish a lean or trimmed worker artifact for v0.19.0. The measured gains are immaterial and
the trimmed variant fails its preservation contract. The existing unified sandbox worker remains the
only supported artifact.

The experiment fixture stays outside `src/` and outside `ETL-SQL.slnx` solely to make the rejected
comparison reproducible. Its `worker-profile.json` is the explicit connector/profile manifest used
by the experiment; it is not a shipped deployment profile.

Reopen the decision only after a source-boundary change removes a substantial shared dependency,
or fleet evidence shows startup memory-duration is a meaningful cost driver. A future proposal must
rerun the same measurements and clear the thresholds before paying the full connector, governance,
cancellation, sandbox, and deployment-profile certification cost.
