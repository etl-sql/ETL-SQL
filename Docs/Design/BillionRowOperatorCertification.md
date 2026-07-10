# Billion-Row Operator Certification (v0.15.0 Phase 4) — Design

**Status:** Implementation in progress; Slice A scenario manifest and report schema are implemented.
**TODO items covered:** v0.15.0 Phase 4 (operator-specific 1B criteria for join/sort first,
then high-cardinality grouping, eligible windows, holistic aggregates, and heterogeneous `MERGE`).
**Completion gate:** every advertised 1B operator has an isolated, resumable certification scenario
and an explicit non-goal list; operators that miss criteria remain unadvertised.

---

## 1. Goal

The v0.14.0 Gate F result proved a narrow, credible billion-row path. Phase 4 broadens coverage
without turning that into a blanket claim. Certification is **operator-specific**: each advertised
shape must have its own admission criteria, workload generator, resource preflight, correctness
oracle, telemetry requirements, failure semantics, and published non-goals.

The first two targets are external equi-join and external sort because they are common, bounded
operators with existing spill implementations. Later targets are admitted only when their native
or spill-backed paths have differential correctness coverage at smaller scale.

### Non-goals

- **No "all SQL at 1B" claim.** Unsupported expressions, adversarial distributions, and row-engine
  fallbacks remain explicit.
- **No certification of destructive mutations without safety gates.** `MERGE` scenarios must use
  isolated generated targets and `WHAT_IF` or transaction rollback where applicable.
- **No reliance on provider services.** Operator certification uses deterministic local generated
  sources unless a provider-specific claim is being made separately.
- **No hidden scale-down.** A scenario that reduces rows after preflight is not a 1B pass.

---

## 2. Certification Matrix

Each operator has one of four states:

| State | Meaning |
| :--- | :--- |
| `Certified` | 1B scenario passes all criteria on the reference machine class |
| `Candidate` | Scenario exists and lower-tier evidence passes, but 1B has not passed |
| `Not certified` | No 1B claim; may still work for smaller workloads |
| `Excluded` | Known semantic or resource reason prevents certification |

Initial Phase 4 matrix:

| Operator shape | Starting state | First required scenario |
| :--- | :--- | :--- |
| External equi-join | Candidate | Two generated sides with controlled key overlap and skew |
| External sort | Candidate | 1B rows, multi-key sort, deterministic checksum over ordered output |
| High-cardinality grouping | Candidate after join/sort | Group count high enough to force partition pressure |
| Eligible window shapes | Candidate later | `ROW_NUMBER`/bounded partition windows only |
| Holistic aggregates | Not certified | Requires exact memory contract per aggregate |
| Heterogeneous `MERGE` | Not certified | Requires bounded source and target staging path |

---

## 3. Scenario Contract

Every billion-row scenario is defined by a manifest committed with the test/harness code.

| Field | Required content |
| :--- | :--- |
| `ScenarioId` | Stable identifier used in resume state and reports |
| `Operator` | Sort, join, aggregate, window, merge |
| `Rows` | Exact generated row counts per source |
| `Shape` | Column types, width, null rate, key distribution, skew |
| `Admission` | Minimum disk, memory ceiling, expected spill path, disabled fallbacks |
| `CorrectnessOracle` | Checksum, count, min/max, sampled ordered checks, or independent smaller oracle |
| `TelemetryContract` | Required metrics and acceptable ranges |
| `NonGoals` | Unsupported variants not covered by this pass |
| `ResumeKey` | Deterministic checkpoint key for operator-run continuation |

The harness must refuse to run a 1B scenario when admission requirements are not satisfied.

---

## 4. Admission Criteria

Admission is checked before any large data is generated.

| Criterion | Rule |
| :--- | :--- |
| Disk free | Required free spill disk = estimated spill bytes × 1.5 + safety reserve |
| Memory | `Engine:TotalMemoryGrantMB` and per-operator grant are recorded and bounded |
| Spill root | Spill volume is local or documented shared storage with measured throughput |
| Runtime mode | Server GC and Release binaries for operator-run lanes |
| Adaptive execution | Off unless the scenario explicitly certifies adaptive behavior |
| Determinism | Random seeds fixed and written into report |
| Native path | If the claim depends on native/columnar path, fallback is a hard failure |

The report must distinguish "not admitted" from "failed after admission." Not-admitted runs do not
count as failures, but they cannot satisfy release criteria.

---

## 5. Operator Criteria

### External Sort

Certification criteria:

- Exactly 1B input rows generated or streamed.
- Sort keys include at least one numeric key and one tie-breaker key.
- Output order is proven by streaming validation, not full materialization.
- `SortSpillCount`, spill bytes, run count, merge pass count, elapsed time, and peak working set
  are recorded.
- Top-N optimized plans are excluded; this is full external sort certification.

Non-goals:

- Locale-specific collation.
- Arbitrary expression sort keys that force row fallback.
- Sorts whose result is consumed by a downstream operator not part of the scenario.

### External Equi-Join

Certification criteria:

- Both sides generated with known cardinality, key overlap, and duplicate factor.
- Result count is mathematically predictable.
- Partition count, repartition pass count, skew factor, spill bytes, and peak working set are
  recorded.
- The harness validates no partition exceeds the documented memory strategy without repartitioning
  or clear failure.

Non-goals:

- Non-equi joins.
- Outer joins with high null-expansion until separately admitted.
- Adversarial single-key skew unless the scenario is explicitly a skew scenario.

### High-Cardinality Grouping

Certification criteria:

- Group count is high enough to exercise partition pressure, not just low-cardinality aggregation.
- Aggregate list includes `COUNT`, `SUM`, `MIN`, and `MAX` on generated numeric columns.
- Correctness is validated by generated-key formulas and sampled groups.

Non-goals:

- Holistic aggregates such as median/percentile until they have a bounded exact or approximate
  design.
- `GROUPING SETS`, `ROLLUP`, and `CUBE` at 1B unless explicitly certified.

### Eligible Window Shapes

Certification criteria:

- Only admitted window functions are certified, starting with `ROW_NUMBER` over deterministic
  partitions/order.
- Partition cardinality and max partition size are recorded.
- Output is validated by per-partition min/max row numbers and sampled order checks.

Non-goals:

- Unbounded frames requiring full partition materialization unless the external window path proves
  bounded behavior for that exact frame.

### Heterogeneous MERGE

Certification criteria:

- Source and target are generated in separate connector/storage contexts.
- Source is staged through `#temp` or another engine-owned bounded path.
- Destructive behavior is guarded by transaction rollback or `WHAT_IF`.
- Match/update/insert counts are predicted and validated.

Non-goals:

- Production mutation claims.
- Provider-specific bulk-update performance.

---

## 6. Reporting and Publication

`Docs/Large_Data_Certification.md` becomes the public matrix. Each certified operator row links to
the exact artifact under `certification-results/`.

Published wording must use this shape:

> Certified for `<operator shape>` at `<row count>` on `<machine class>` using `<scenario id>`.
> Excludes `<non-goals>`.

Release notes may not say "1B SQL support" unless every broad operator family has passed, which is
not a v0.15.0 goal.

---

## 7. Delivery Plan

1. **Slice A — scenario manifest and report schema.** Add scenario metadata, admission results,
   and non-goals to Gate F reports. *(Implemented: the checked-in scenario manifest lives at
   `certification-results/billion-row-operator-scenarios.json`, manifest validation tests guard
   external sort/join admission and success criteria, and future Gate F reports emit
   `scenarioManifests` plus `admission` sections.)*
2. **Slice B — external sort 1B candidate.** Build generator, streaming order validator, resume
   points, and admission preflight. *(In progress: `Test-GateF.ps1 -Scenario ExternalSort`
   now runs an explicit candidate path with deterministic generated rows, multi-key sort,
   streaming order validation, resume key, disk admission, and report evidence. It remains
   Candidate until an operator-run artifact passes at 1B and the public matrix is updated.)*
3. **Slice C — external equi-join 1B candidate.** Build two-source generator, result-count oracle,
   skew telemetry, and repartition metrics.
4. **Slice D — publish matrix update.** Update `Docs/Large_Data_Certification.md` to advertise
   only scenarios that passed.
5. **Slice E — later operators.** Add grouping/window/MERGE candidates only after lower-tier
   differential tests pass.

---

## 8. Test Plan

| Layer | Proves |
| :--- | :--- |
| Manifest validation tests | Every candidate declares admission, telemetry, and non-goals |
| Generator tests | Small row counts produce exact predictable checksums/counts |
| Lower-tier differential tests | Native/spill path matches row-engine oracle before 1B run |
| Admission tests | Low disk, wrong config, or fallback route refuses certification |
| Resume tests | Interrupted scenario resumes without duplicating or skipping work |
| Report tests | Certified matrix contains only passed scenarios |

---

## 9. Completion Criteria

- External sort and equi-join have explicit pass/fail artifacts or remain unadvertised.
- Every new 1B claim has an artifact, machine class, and non-goal list.
- Unsupported shapes are documented in the certification matrix.
