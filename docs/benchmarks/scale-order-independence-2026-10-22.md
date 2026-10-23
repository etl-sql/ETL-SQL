# Smoke scale order-independence check — 2026-10-22/23

**Latest result (2026-10-23): all 78 comparisons pass existing failure bands after per-scenario
process isolation.** See the [follow-up](#process-isolation-follow-up--2026-10-23). Baseline
calibration remains pending.

**Initial result (2026-10-22): not achieved.** One untimed execution of each scenario's own path
did not make shared-host Smoke measurements agree with isolated runs. Ten of thirteen scenarios
exceed their existing elapsed-time failure band in at least one comparison direction.
No baseline or band was changed. This is harness validation, not an engine regression claim.

## Method and identity

The Release fixture was built after browser preparation and its check with Node 22.23.3.
On one Windows X64 host running .NET 10.0.13, capture five samples of the complete Smoke tier,
then five samples of each of its thirteen scenarios in fresh test hosts, then five more samples
of the complete tier. Each capture retains the normal discarded process run and
`own-path-untimed-v1` scenario warm-up. All 195 measured scenario executions passed correctness
and the fixture's resource assertions.

The source was commit `9493a79821a0d089ba569dd456d48e6da97717bf` plus the existing working-tree
changes. Its release source fingerprint was
`b3f5f9eca058b63085ad7b5904dbdc2c3ebac240d1759048fbd1607b4a575352` throughout capture.
Every report's source fingerprint was checked against that identity and its own recorded config.
The fixture definition hash was
`82ff31fcbe89ab27b4f5346013978a886ac14890af791d5e5ef2cf340010e5ba`.
This dirty-tree investigation is not clean candidate/control baseline calibration.

For each scenario, derive reports containing elapsed-time medians, row count, result count,
checksum and pass status. Keep the original reports and samples intact. Run the existing
`Compare-CertBaseline.ps1` in both directions against each bracketing tier, using its default
family bands. Also compare the two full-tier captures in both directions. Resource peaks are
not compared across shared and isolated process lifetimes in this timing investigation.

## Observations

All values are five-sample elapsed-time medians in milliseconds. A mismatch means at least one
isolated-versus-tier direction exceeds the existing failure band.

| Scenario | Tier before | Isolated | Tier after | Band | Mismatch |
| :--- | ---: | ---: | ---: | ---: | :---: |
| CsvIngest | 28 | 46 | 27 | 35% | Yes |
| CubeGroupingSets | 894 | 1177 | 835 | 25% | Yes |
| ExternalAggregate | 190 | 430 | 197 | 25% | Yes |
| ExternalJoin | 584 | 646 | 653 | 25% | No |
| ExternalSort | 315 | 464 | 380 | 25% | Yes |
| ParquetRoundTrip | 107 | 35 | 109 | 35% | Yes |
| ReportDatasetSnapshotReload | 349 | 265 | 335 | 35% | No |
| ScalarSubqueryCache | 378 | 527 | 396 | 25% | Yes |
| SpillCleanupFailure | 16 | 39 | 16 | 20% | Yes |
| SpillCleanupSuccess | 19 | 53 | 19 | 20% | Yes |
| StreamingSelect | 401 | 450 | 399 | 15% | No |
| TempTableSpill | 27 | 72 | 28 | 20% | Yes |
| WindowFunction | 486 | 980 | 492 | 25% | Yes |

Nineteen of the 52 isolated-versus-tier directional comparisons fail. All 26 tier-versus-tier
directional comparisons pass. Differences go in both directions, so the evidence does not
support a single uniform host slowdown. The mechanism remains unproven.

## Retained evidence and next check

Local evidence is retained outside the candidate sources at
`release-validation/scale-order-20261022/`: fifteen original `cert-report.json` reports, their
raw output and sample distributions, `source.json`, the capture/comparison scripts,
`order-independence.json`, and all 78 comparator logs under `timing-comparisons/`.

This first investigation left the order-independence TODO open. The follow-up below changes
process isolation and reruns the complete comparison. Clean candidate/control calibration on the
same host remains required before replacing the Smoke and Standard baselines.

## Repeated-warm-up pilot

A copied fixture then performed at least five own-path warm-ups, targeting one second including
cleanup, with a twenty-iteration cap. It retained the normal assertion and root cleanup behavior.
Three samples of the complete tier and three samples of three isolated cases produced:

| Scenario | Full tier (ms) | Isolated (ms) |
| :--- | ---: | ---: |
| ExternalAggregate | 165 | 316 |
| TempTableSpill | 28 | 60 |
| ParquetRoundTrip | 12 | 52 |

All three still exceed their existing bands. Repeating the scenario a few times is insufficient;
the experimental fixture was not adopted. Its captures and copied source are retained at
`release-validation/scale-warmup-repeat-20261022/`. No baseline comparison should mix that copied
fixture definition with the original fixture.

## Process isolation follow-up — 2026-10-23

Smoke and Standard capture now launch each scenario's exact test method in a fresh host for
every sample. Explicit scenario selection uses the same filter and process model. Each child
retains its raw output and its invocation, exit code and metrics in `sampleReports[].children`.
Reports record `processIsolation: single-scenario-process-v1`; the comparator rejects older
shared-host captures. The one untimed own-path operation is retained.

On the same Windows X64/.NET 10.0.13 host, repeat the complete five-sample protocol: a full tier,
each of thirteen isolated scenarios, and a final full tier. All 195 measured executions pass
correctness and resource assertions. VSTest discovery agrees with all thirteen planned methods.

| Scenario | Tier before (ms) | Isolated (ms) | Tier after (ms) |
| :--- | ---: | ---: | ---: |
| CsvIngest | 49 | 47 | 48 |
| CubeGroupingSets | 1198 | 1348 | 1459 |
| ExternalAggregate | 414 | 419 | 439 |
| ExternalJoin | 584 | 630 | 654 |
| ExternalSort | 379 | 406 | 470 |
| ParquetRoundTrip | 37 | 34 | 36 |
| ReportDatasetSnapshotReload | 269 | 257 | 272 |
| ScalarSubqueryCache | 559 | 523 | 543 |
| SpillCleanupFailure | 39 | 38 | 39 |
| SpillCleanupSuccess | 57 | 57 | 60 |
| StreamingSelect | 434 | 460 | 435 |
| TempTableSpill | 71 | 69 | 71 |
| WindowFunction | 1056 | 942 | 963 |

All 52 isolated/tier directional comparisons and all 26 tier/tier comparisons pass the existing
failure bands. Five latency comparisons cross warning thresholds, involving CubeGroupingSets,
ExternalSort and WindowFunction; those warnings are retained. Hardware mismatch suppresses no
failure. Capture settings match apart from scenario/filter selection, and the source fingerprint
was unchanged throughout capture.

The captured working tree was based on commit `9493a79821a0d089ba569dd456d48e6da97717bf`, with
release source fingerprint `762e8dbf44d82874d5cb5edef7c0645b59f6cc8057854cd893c80de16145dece` and
fixture definition `7845fd8e94eb1552a4ba34bff3a97a2675a6e1ebc022216da9b29d4c7001569f`.
Local evidence at `release-validation/scale-isolation-20261023/` retains all fifteen original
reports and raw logs, `source.json`, `order-independence.json`, 78 comparator logs and the VSTest
discovery coverage receipt. A subsequent input-case normalization fix preserves the exact
thirteen-filter inventory and timed fixture; focused tests verify that equivalence.

A separate functional Standard capture with two samples also passes all thirteen scenarios at
row scale 10, including correctness and resource assertions. Its 26 measured cases and child
records are retained under `standard-functional/` in the same evidence directory. That capture
does not replace clean candidate/control baseline calibration.

References:

- [Historical Smoke deviation](../releases/v0.21.0-performance-results.md)
- [Performance comparison policy](../architecture/decisions/performance-regression-quality.md)
- [Scale runner](../../scripts/Test-ScaleCertification.ps1)
- [Baseline comparator](../../scripts/Compare-CertBaseline.ps1)
