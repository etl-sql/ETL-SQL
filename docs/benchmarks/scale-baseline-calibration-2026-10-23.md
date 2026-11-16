# Smoke and Standard baseline calibration — 2026-10-23

The v0.22.0 baseline update passes with the scaled warm-up protocol. All 1,170 measured
correctness/resource checks, 24 full comparisons and 78 Smoke order comparisons pass. Both
predetermined candidate references are promoted, with previous references archived byte for byte.
See [scaled warm-up calibration](#scaled-warm-up-calibration) for the accepted evidence and warnings.

The first calibration attempt below failed one of twenty-four comparator checks despite passing
all 1,040 measured correctness/memory checks. Its baselines were not promoted. Its
[curated results](../../certification-results/scale-calibration-2026-10-23.json)
retain capture hashes, source/config fingerprints, scenario summaries, temp-table samples and
every comparison outcome.

## Method

Run each full tier in control / candidate / candidate / control order, with ten measured samples
of all thirteen scenarios per capture. Smoke uses row scale 1; Standard uses row scale 10. Every
scenario runs in a fresh test host after a discarded process warm-up and its own untimed operation.

- Control: published `v0.21.0`, peeled commit `53a3343d449845c11c7f144cf5b305d07e2e2b7f`.
- Candidate: `74f32e8843e122ba79f63f89ad60e9a732295dd8`.
- Frozen fixture definition: `7845fd8e94eb1552a4ba34bff3a97a2675a6e1ebc022216da9b29d4c7001569f`.
- Host: Windows 11 X64, .NET 10.0.13, Core Ultra 9 275HX, 24 logical cores, 32 GB RAM and NVMe.
- Release build; server GC requested and observed throughout.

The same frozen runner, comparator, helpers and fixture run against both clean commits. Engine
sources, shared build/dependency inputs and the comparator are identical between these commits.
For each tier, compare all six pairs in both directions using the existing complete metric checks.
No hardware mismatch suppresses a failure.

The first candidate capture was selected in advance as the proposed reference for each tier.
Neither reference is promoted because the complete calibration did not pass.

## Results

| Tier | Measured correctness/memory checks | Comparator checks | Outcome |
| :--- | ---: | ---: | :--- |
| Smoke | 520 / 520 | 12 / 12 | Pass with two warning metrics. |
| Standard | 520 / 520 | 11 / 12 | Control-repeat GC pause failure; one memory warning. |

Every candidate/control comparison and both candidate-repeat directions pass. Standard's first
control compared with its final control fails for `TempTableSpill_500000_SELECT_INTO`: median GC
pause rises from 50.3 ms to 70.65 ms, a 40.5% increase against the unchanged 35% failure band.
The baseline spread is 17.1 ms. The reverse comparison passes.

| Standard capture | Median elapsed (ms) | Median GC pause (ms) | Allocated (MB) | Gen 2 collections per sample |
| :--- | ---: | ---: | ---: | :--- |
| First control | 533.5 | 50.3 | 317.85 | 3–4 |
| First candidate | 569.5 | 58.15 | 317.8 | 3–6 |
| Repeat candidate | 572 | 66.9 | 317.8 | 3–6 |
| Final control | 574.5 | 70.65 | 318 | 3–6 |

The table describes the same 500,000-row temp-table workload in four independent captures.
Its allocation totals stay close while collection counts and pause time vary. The cause is
unproven. Investigate the child's active GC configuration and heap/memory-pressure state before
another full calibration. Use focused scenario probes first; retain this failed run.

The first-to-final Standard control comparison also warns for WindowFunction peak working set:
444.6 MB to 493.7 MB, up 11.0% against the 10% warning band. Smoke's first-candidate-to-final-control
comparison warns for StreamingSelect elapsed time (423.5 to 464 ms, up 9.6%) and throughput
(236135.75 to 215518.25 rows/s, down 8.7%), both against 8% warning bands. These are the only warnings.

## Retained evidence

Local raw evidence is under
`release-validation/calibration-preparation-20261023/captures-20261023T125851-b190b150/`:
the frozen harness inventory, eight original reports and raw child logs, twenty-four comparator
logs and Markdown reports, and `calibration.json` with `passed: false`. The controller exits 1
before creating proposed reference copies. The independent review also refuses a failed result.

After the first successful Smoke control capture, the initial controller stopped because it
compared the annotated tag object's ID with the report's peeled commit ID. The resumed controller
normalizes inputs with `^{commit}`, revalidates and reuses that completed capture. The initial
controller and stop record are retained. No passing measurement was repeated to improve its result.

## Focused warm-up probe

A diagnostic copy of the fixture compares the current 50,000-row warm-up with a 500,000-row
warm-up for the Standard temp-table scenario. Run Smoke-sized / Standard-sized / Standard-sized /
Smoke-sized warm-ups, ten fresh-process measured samples per capture. Both variants emit GC
configuration and memory state after measured resource capture. Product sources remain unchanged.
The [probe record](../../certification-results/scale-warmup-probe-2026-10-23.json) retains all forty
sample outcomes and GC snapshots.

| Warm-up capture | Median elapsed (ms) | Median GC pause (ms) | GC pause p05–p95 (ms) | Peak working set (MB) | Gen 2 collections per sample |
| :--- | ---: | ---: | :--- | ---: | :--- |
| Smoke-sized, first | 522.5 | 50.7 | 44.35–59.37 | 241.3 | 3 |
| Standard-sized, first | 480.5 | 44.7 | 41.545–46.8 | 278.1 | 1–2 |
| Standard-sized, repeat | 510 | 47.4 | 41.975–50.065 | 288.8 | 1–2 |
| Smoke-sized, final | 544 | 54.9 | 50.925–58.495 | 245 | 3 |

All forty measured correctness/memory checks pass. Each warm-up profile's repeat comparison passes
both directions, with no warnings. The comparator rejects comparisons across the two fixture
definitions. The table is descriptive; it does not authorize replacing a baseline across profiles.

Runtime snapshots report server and concurrent GC enabled, heap count 24, dynamic adaptation mode
1 and no explicit heap hard limit. Reported GC memory load ranges from approximately 16.2 to
18.2 GB during the probe. Standard-sized warm-up reduces collections in this scenario while raising
the retained working set. The original control-repeat GC failure does not recur in either profile,
so this probe does not establish its cause or prove a stable full-tier replacement.

Evidence is retained at
`release-validation/calibration-preparation-20261023/warmup-probe-20261023T183527/`. Two earlier
preparation attempts stopped before measurement: sandbox NuGet access was denied, then the
restored candidate checkout needed browser-asset preparation. Their logs remain intact. No
measured sample was retried. These results motivated warming the measured Standard workload and
recording GC state. Full calibration remains required before baseline promotion.

The harness now records `own-path-scaled-untimed-v2`, warms through Standard scale (capping larger
tiers at 10x), and records completed warm-up scale, selected GC configuration and post-measurement
GC memory state. Captures reject incomplete warm-up provenance or changing GC settings; comparisons
reject different protocol versions or GC configurations. Historical captures retain their version.

Native [functional validation](../../certification-results/scale-warmup-functional-2026-10-23.json)
of this implementation passes two full samples of each tier: all 52
measured checks use the expected warm-up scale and absolute peak-memory gate, with retained GC
state. Evidence is under
`release-validation/calibration-preparation-20261023/new-protocol-functional-20261023T185428/`.
The source tree was dirty during this functional check. It is not clean calibration evidence.

## Scaled warm-up calibration

The clean follow-up uses candidate `040d2c217774bb02223ed4bc078990d09871b956` and the same published
v0.21.0 control, on the host described above. Both use the frozen fixture
`dc848d37fae4a1725dec9a3e6f24a2caf27fcc51d18ea908328dbefc4a99b723`, protocol
`own-path-scaled-untimed-v2`, observed GC configuration and unchanged comparison bands. Engine,
dependency and comparator inputs are identical between the two commits.

Each tier runs control/candidate/candidate/control, ten samples of thirteen scenarios per capture.
Between the two full candidate Smoke captures, thirteen isolated Smoke captures each measure ten
samples. All captures use fresh scenario processes, discarded process warm-up and the same
completed own-path warm-up scale. Independent review checks every child exit, exact filter,
correctness result, absolute peak-memory gate, clean source/config binding and stable runtime/GC
profile. No measured sample was retried or omitted.

| Scope | Checks | Outcome |
| :--- | ---: | :--- |
| Full Smoke measured correctness/resources | 520 / 520 | Pass |
| Full Standard measured correctness/resources | 520 / 520 | Pass |
| Isolated Smoke measured correctness/resources | 130 / 130 | Pass |
| Full-metric comparisons, all six pairs in both directions per tier | 24 / 24 | Pass, one working-set warning |
| Smoke order comparisons, six directions per scenario | 78 / 78 | Pass, one timing warning |

The original Standard temp-table GC-pause failure does not recur. Parquet round-trip and report
dataset snapshot reload pass every applicable full and order comparison. This supports replacing
the incompatible shared-host references; it does not establish a cause for the earlier GC failure.

The one full-metric warning is Standard cube peak working set, candidate repeat (capture 3) versus
first control (capture 1): 383 MB to 425.6 MB, +11.1% against the unchanged 10% warning band, with
32.8 MB baseline spread. No metric crosses a failure band. The 52 full/isolated order comparisons
also retain expected configuration-fingerprint warnings: independent review proves the configuration
differs only in scenario/filter selection. One order comparison warns for scalar-subquery elapsed
time, second full candidate versus first full candidate: 487 ms to 546 ms, +12.1% against the 12%
warning band. All warnings remain in the curated evidence; there are no hardware suppressions.

The references were selected before measurement: full candidate capture 2 for each tier, copied
without pooling, transformation or band edits. Exact `.gitattributes` rules disable line-ending
conversion for these references and their archived predecessors, preserving the recorded hashes
when staged or checked out on another platform. Their SHA-256 hashes are:

- **Smoke:** `de59e4e11bf0b29b3e2dda1214316a3303771da1e9bc573bd2f81e30aa41e004`
- **Standard:** `4ac752b1d2f9971996b175baeebb826b642fd1407a6bdbf503573e0286c72c80`

The [curated review and promotion record](../../certification-results/scale-calibration-v2-2026-10-23.json)
retains all capture hashes, scenario summaries, full comparison warnings, order comparisons,
timing-view/original hashes, source/config fingerprints and old/new reference hashes. Original
October 4 baselines remain under [historical references](../../certification-results/historical-baselines/README.md).
Raw reports, child logs, frozen harness inventory, controller, replacement plan and independent
review remain at
`release-validation/calibration-preparation-20261023/captures-v2-20261023T192353-3dda32c3/`.
The independent review was repeated before promotion, and all 24 full comparisons were replayed
with the current comparator. Final v0.22.0 release certification must still run on the shipping SHA.

References:

- [Smoke order-independence proof](scale-order-independence-2026-10-22.md)
- [Scale runner](../../scripts/Test-ScaleCertification.ps1)
- [Comparator](../../scripts/Compare-CertBaseline.ps1)
- [.NET GC configuration](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector)
