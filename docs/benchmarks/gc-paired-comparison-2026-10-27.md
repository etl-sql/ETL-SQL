# v0.22.0 GC failure investigation

Ten-sample normal-project probes reproduce both GC reference failures. Paired captures also show
that the old source control is elevated on the current runtime/workstation. The cause remains
unproven and the original full workstation gate remains failed. These are
development measurements for v0.22.0; the measured packages still report version 0.21.0.

## Measurements

GC pause medians in milliseconds:

| Scenario | Stored reference, .NET 10.0.13 | Source control, .NET 10.0.14 | Candidate, .NET 10.0.14 | Original failed gate, .NET 10.0.14 |
| :--- | ---: | ---: | ---: | ---: |
| Spill cleanup, 50k rows | 4.8 | 9.2 | 7.7 | 15.1 |
| Temp-table spill, 50k rows | 4.7 | 8.3 | 10.2 | 11.1 |

The stored reference has ten samples. Each other column has three. The control uses
`040d2c217774bb02223ed4bc078990d09871b956`; the candidate and original failed gate use
`d9e137c52715e533a40bfe5db51a37a4fbb61079`.

Paired cleanup elapsed medians are 51 ms for the control and 50 ms for the candidate. Temp-table
medians are 63 ms and 65 ms. All twelve measured correctness and memory checks pass with exact
row counts/checksums. Allocations remain close: cleanup 29.6/29.3 MB and temp-table 30.4/30.3 MB
for control/candidate.

The GC ranges overlap. Cleanup ranges are 7.2–10.0 ms for the control and 4.2–10.2 ms for the
candidate; temp-table ranges are 4.5–9.8 ms and 4.1–10.6 ms. Three samples per arm do not establish
a stable change or identify why the original run exceeded its stored reference.

## Follow-up failures and normal-project control

The predetermined ten-sample probes use the unchanged clean candidate and the normal project.
Both fail the unchanged reference comparison while all twenty correctness/memory checks pass:

| Scenario | GC median | Reference comparison |
| :--- | ---: | :--- |
| Spill cleanup | 14.7 ms | Failed, +206.2% |
| Temp-table spill | 11.6 ms | Failed, +146.8% |

An additional ten-sample temp-table control uses the captured fixture with the main checkout and
working directory held constant. Its median is 8.65 ms. No reference-comparison command was run
for that context control.

The final `A,B,B,A,A,B` experiment uses the normal test project in the main checkout for both
commits. Each arm verifies the same fixture hash, restores/builds the project and checks clean
source before/after capture. The controller restores the original branch in `finally`.
Temp-table GC medians are 12.1 ms for the old source and 15.5 ms for the candidate; elapsed medians
are 70 ms and 73 ms. GC ranges overlap at 9.8–18.6 ms and 12.9–15.9 ms. All six correctness/memory
checks pass. The old source control is also well above the stored 4.7 ms reference on this machine.

Static inspection finds matching candidate revisions, Release flags and method IL hashes for
Core, Engine, App and the test fixture between the normal and captured builds. Runtime config
and application-config bytes match too. This excludes obvious stale/debug-build or config
differences in those inspected files; it does not establish identical live process state.

## Method and retained evidence

Each scenario uses the predetermined sequence `A,B,B,A,A,B` in one worktree, rebuilding after
each checkout. Browser assets are prepared and verified for that checkout before building. A
discarded process run primes file caches; every measured host runs its own untimed scenario
warm-up. Each capture reports a clean source commit, Release configuration, 50k rows and warm-up
row scale 1.

All captures, including the stored reference and original failed run, use fixture hash
`dc848d37fae4a1725dec9a3e6f24a2caf27fcc51d18ea908328dbefc4a99b723`.
The observed GC configuration matches: server GC, 24 heaps, dynamic adaptation mode 1 and no
heap hard limit. Both paired arms run on .NET 10.0.14 on the same Windows x64 workstation.
No other repository validation jobs overlap the paired captures. Three load readings before each
comparison show 0–10% CPU and about 19 GiB free memory.

The [curated audit](../../certification-results/gc-diagnostic-2026-10-27.json) retains the paired
samples, failed follow-ups, source identity, fixture/reference/report hashes and recomputed
medians. The executable local audit checks all 48 measured cases, including the failed comparison
exits, clean source metadata and equality between raw paired reports and summaries.

Raw evidence remains under `scratch/smoke-gc-comparison/release-validation/`:

- `gc-cleanup-comparison/` and `gc-temp-comparison/`: captured fixtures, reports and child logs.
- `gc-quiet-host-preflight.json` and `gc-temp-quiet-host-preflight.json`: initial load readings.
- `verify-gc-comparisons.mjs` and `gc-comparison-audit.json`: executable audit and result.
- `pe-inspection.json`: static assembly metadata and method-body hashes.

The main checkout also retains `release-validation/workstation-certification/`:

- `gc-ten-sample-probes/`: both failed reference comparisons and twenty measured samples.
- `captured-fixture-control/`: ten additional context-control samples.
- `normal-project-paired-control/`: six normal-project captures, controller and branch-restoration receipt.

## Release status

The original gate's three failures and skipped coverage stay recorded. These paired captures do
not authorize publication or replace any reference. The experiments do not isolate runtime
effects from other workstation or capture-context conditions. Keep the stored baselines and comparison bands
unchanged and run the complete gate on the final clean candidate.

References:

- [Scale baseline calibration](scale-baseline-calibration-2026-10-23.md)
- [Commit-comparison runner](../../scripts/Test-ScaleCommitComparison.ps1)
- [Release checklist](../releases/release-checklist.md)
