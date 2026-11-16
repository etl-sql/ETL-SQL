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

## Remaining work

Keep TODO §1.9 open until a controlled experiment explains the stored-reference gap or an
explicitly justified baseline recalibration is accepted. Retain source/configuration hashes,
per-sample elapsed time and GC pauses, runtime/GC settings, warm-up protocol and host readiness
for any follow-up. Preserve the existing baseline and failed comparisons.

References:

- [Paired GC investigation](gc-paired-comparison-2026-10-27.md)
- [Fixed-binary runtime audit](../../certification-results/gc-runtime-control-2026-10-27.json)
- [Scale baseline calibration](scale-baseline-calibration-2026-10-23.md)
- [Scale certification runner](../../scripts/Test-ScaleCertification.ps1)
- [Baseline comparator](../../scripts/Compare-CertBaseline.ps1)
