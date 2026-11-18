# Smoke Spill GC-Pause Investigation (2026-11-16)

The stored-reference gap remains unexplained. The retained fixed-binary controls do not support
attributing it to a .NET 10.0.14 runtime regression. No baseline or failure band is changed by
this investigation.

## Retained evidence

The [Smoke baseline](../../certification-results/baseline-smoke.json) captured on 2026-10-23
records 4.7 ms GC pause for `TempTableSpill_50000_SELECT_INTO` and 4.8 ms for
`SpillCleanupSuccess_50000`. Later captures exceed those references, including captures using
the older source. See the [paired investigation](gc-paired-comparison-2026-10-27.md) for the
source, fixture, configuration and capture controls.

The [fixed-binary runtime audit](../../certification-results/gc-runtime-control-2026-10-27.json)
runs the same test binary and VSTest console through five A/B/B/A blocks per scenario, with ten
samples per runtime. All 40 correctness and memory cases pass. Both runtimes observe Server GC,
24 heaps and `dynamicAdaptationMode=1`.

| Scenario | .NET 10.0.13 GC median | .NET 10.0.14 GC median |
| :--- | ---: | ---: |
| Temp-table spill | 13.2 ms | 12.55 ms |
| Spill cleanup | 15.55 ms | 15.4 ms |

Both runtimes reproduce the higher pauses. Comparing a historical .NET 10.0.13 baseline to a
later .NET 10.0.14 capture does not isolate the runtime as the cause.

## Limits of the newer diagnostic claim

The 2026-11-16 implementation report described three .NET 10.0.15 cleanup samples with DATAS
disabled: 0 ms measured GC pause and 40–42 ms elapsed. That is a reported diagnostic observation;
this note does not include a retained capture with source/configuration fingerprints and raw
sample hashes for those runs. Even with that evidence, preventing collections during a short
sample would establish sensitivity to GC configuration, not explain the historical gap when
both the baseline and subsequent runtime controls used DATAS.

Subtracting independently computed elapsed and GC medians is also not a measurement of median
non-GC execution time. Compute the difference per sample first, then summarize those values.

## Closed by policy (2026-11-18)

TODO §1.9 is closed by the owner-approved W6 policy, not by a root cause
([W6 design note](../releases/v0.23.0-w6-performance-gates-design.md)):

- GC pause and GC counts are machine metrics. They are reported everywhere and gate nowhere.
  Across five hosted replicas at one commit, GC-pause medians spread 19–83%, far more than the
  5–30 ms Smoke spill gaps this note tracked.
- Real regressions still surface: as elapsed time in the paired A/B release gate (same runner,
  A,B,B,A blocks, fails at a 10% median ratio), as allocation growth (+5% fails against hosted
  baselines), or as extra spill work (exact counters).
- The workstation baselines that produced the gaps were replaced by hosted baselines in W6
  slice 2. They stay in history with the failed comparisons, as this note asked.

The open question (why the stored reference paused less than later runtime controls) is not
pursued. Reopen only if a paired A/B run or allocation gate points at GC behaviour.

References:

- [Paired GC investigation](gc-paired-comparison-2026-10-27.md)
- [Fixed-binary runtime audit](../../certification-results/gc-runtime-control-2026-10-27.json)
- [Scale baseline calibration](scale-baseline-calibration-2026-10-23.md)
- [Scale certification runner](../../scripts/Test-ScaleCertification.ps1)
- [Baseline comparator](../../scripts/Compare-CertBaseline.ps1)
