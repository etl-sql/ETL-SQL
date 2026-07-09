# Performance Regression Quality (v0.15.0 Phase 3) — Design

**Status:** Implementation in progress; Slice A/B foundations are partially implemented.
**TODO items covered:** v0.15.0 Phase 3 (scenario-specific warning/failure bands;
runtime/hardware/config/commit metadata; current-commit performance evidence before publishing).
**Completion gate:** performance regressions are detected by scenario-specific, statistically
meaningful comparisons instead of a single catastrophe-only throughput floor.

---

## 1. Goal

The scale lane currently proves correctness, spill behavior, and broad memory containment. Its
performance checks are intentionally coarse: a catastrophic throughput floor catches broken paths,
but it cannot distinguish real scenario regressions from noise, machine differences, or one lucky
run. Phase 3 turns scale output into a release-quality regression gate.

The target is a **baseline-aware comparator** that reads the existing `CERT_METRIC` artifacts,
adds machine/run metadata, and applies per-scenario warning and failure bands. The gate should
fail only when the evidence is meaningful enough to act on, and should warn when the run deserves
review but is not release-blocking.

### Non-goals

- **No universal rows/sec claim.** Throughput is scenario- and hardware-specific; the comparator
  reports deltas against checked-in baselines.
- **No automatic baseline blessing.** A maintainer must explicitly bless a known-good run.
- **No replacement for allocation budgets.** `Compare-AllocBudget.ps1` remains the memory/GC
  containment gate for the Gate F `#temp` round trip.
- **No default failure on missing baselines for new scenarios.** New scenarios warn until a
  baseline is intentionally added.
- **No comparison across incompatible machines.** The comparator should refuse or warn when the
  baseline and current hardware profiles differ beyond documented tolerances unless explicitly
  overridden.

---

## 2. Existing Machinery

| Mechanism | Current role | Phase 3 change |
| :--- | :--- | :--- |
| `scripts/Test-ScaleCertification.ps1` | Runs scale scenarios and parses `CERT_METRIC` JSON | Adds repeated samples and metadata capture options |
| `scripts/Compare-CertBaseline.ps1` | Compares current `cert-report.json` with checked-in baseline | Replaced or extended with scenario bands, variance, and metadata checks |
| `certification-results/baseline-*.json` | Checked-in baseline artifacts | Gains schema version, hardware profile, config fingerprint, sample distribution |
| `scripts/Test-ScaleBaseline.ps1` | Process-isolated baseline capture | Becomes the preferred blessing workflow for release baselines |
| `scripts/Test-PreRelease.ps1` | Calls smoke/standard scale and baseline checks | Enforces warning/failure policy for selected release lanes |
| `Compare-AllocBudget.ps1` | Fails allocation/GC/peak-memory regressions | Remains independent; throughput bands do not excuse containment regressions |

---

## 3. Baseline Schema

Baseline files remain JSON under `certification-results/`, but their contents become explicit and
versioned.

```jsonc
{
  "schemaVersion": 2,
  "tier": "Standard",
  "capturedAtUtc": "2026-07-09T00:00:00Z",
  "commit": "abcdef1234",
  "sourceFingerprint": "...",
  "host": {
    "machineClass": "workstation-32c-128gb-nvme",
    "os": "Windows",
    "architecture": "x64",
    "processorCount": 32,
    "totalAvailableMemoryBytes": 137438953472,
    "dotnetVersion": "10.0.x",
    "storageClass": "local-nvme"
  },
  "config": {
    "batchSize": 10000,
    "operatorMemoryGrantMB": 256,
    "totalMemoryGrantMB": -1,
    "externalHashPartitions": 32,
    "spillFormat": "Arrow",
    "adaptiveEnabled": false
  },
  "scenarios": [
    {
      "name": "ExternalSort",
      "rowCount": 500000,
      "samples": 5,
      "elapsedMs": { "median": 1200, "p95": 1280, "min": 1170, "max": 1305 },
      "rowsPerSecond": { "median": 416667, "p05": 390625 },
      "peakWorkingSetMB": { "max": 420 },
      "spilledBytes": { "median": 123456789 },
      "correctness": { "passed": true, "checksum": "..." },
      "bands": { "warnPct": 10, "failPct": 20, "minSamples": 3 }
    }
  ]
}
```

Only stable, low-cardinality environment fields belong in the comparison key. Raw usernames,
absolute temp paths, and hostnames should be omitted or normalized.

---

## 4. Comparison Policy

Each scenario compares the current run to the matching baseline by `(tier, scenario name, row
count, config fingerprint)`.

| Metric | Warning | Failure | Notes |
| :--- | :--- | :--- | :--- |
| Correctness/pass status | Any previously passing scenario fails | Same | Correctness always dominates performance |
| `elapsedMs.median` | > baseline by scenario `warnPct` | > baseline by scenario `failPct` | Primary latency metric |
| `rowsPerSecond.median` | < baseline by `warnPct` | < baseline by `failPct` | Equivalent direction to elapsed |
| `peakWorkingSetMB.max` | > baseline by 10% | > baseline by 15% | Mirrors containment concern; allocation budget may be stricter |
| `TotalSpilledBytes` | > baseline by 25% | > baseline by 50% | Warns on accidental extra passes; tolerate compression variance |
| `Gen2`/GC pause | > baseline by 20% | > baseline by 35% | Use Phase 1 budget thresholds when profile report exists |

Default scenario bands:

| Scenario family | Warn | Fail | Reason |
| :--- | ---: | ---: | :--- |
| Streaming scan/filter/projection | 8% | 15% | Low algorithmic variance |
| Temp-table spill round trip | 10% | 20% | I/O and GC sensitivity |
| External sort/join/aggregate/window | 12% | 25% | Spill and partition-shape sensitivity |
| Provider/Docker-backed scenarios | 20% | 35% | External service variance |

The comparator reports all warnings and failures. Failures return exit code 1. Warnings return
exit code 0 but are written into the Markdown report and pre-release state.

---

## 5. Repeated Samples

Phase 3 adds repeated sample support for release-quality lanes:

- Smoke default: one sample, because it is a fast guard.
- Standard default: three samples for scenarios with a baseline.
- Manual baseline capture: five samples.
- Gate F/operator-run: one sample is acceptable because each scenario is long-running, but the
  report must record hardware/config and must compare against an operator-run baseline captured on
  the same machine class.

Comparison uses median for elapsed/throughput and max for containment. If fewer than the scenario's
`minSamples` are present, the comparator emits a warning and uses the available sample; it does not
fail solely for sample count unless the lane explicitly requires repeated samples.

---

## 6. Hardware and Config Compatibility

The comparator must classify the run before comparing:

| Compatibility | Behavior |
| :--- | :--- |
| Same machine class and same config fingerprint | Full compare; warnings/failures enabled |
| Same machine class, config differs only in documented non-performance keys | Full compare with a config note |
| Processor count or available memory differs by >10% | Warn and skip performance failure unless `-AllowHardwareMismatch` |
| Storage class differs | Warn and skip I/O-heavy scenario failure |
| Runtime or OS major version differs | Warn; failure still allowed only when multiple samples agree |

The machine class is a checked-in string, not guessed from hostname. Local runs without a class
may still produce reports, but pre-release failure gates require a class.

---

## 7. Delivery Plan

1. **Slice A — metadata and schema v2.** Extend scale reports with schema version, commit/source
   fingerprint, host profile, and config fingerprint. Preserve compatibility with schema v1
   baselines. *(Partial: report-level metadata is emitted; repeated-sample scenario distributions
   remain Slice C.)*
2. **Slice B — comparator bands.** Extend `Compare-CertBaseline.ps1` with scenario families,
   warning/failure bands, and Markdown output. Keep missing-baseline behavior as warning-only.
   *(Implemented for schema v1 flat metrics and schema v2 metric objects.)*
3. **Slice C — repeated samples.** Add `-Samples` to `Test-ScaleCertification.ps1` and teach the
   parser to aggregate samples by scenario. *(Implemented: runner-level repeated samples, median/max
   summary fields, distribution fields, raw sample metrics, and sample-count comparator warnings.)*
4. **Slice D — pre-release integration.** Update `Test-PreRelease.ps1` so smoke/standard lanes
   enforce the new policy and record warnings in `release-validation/latest/state.json`.
   *(Partial: smoke/standard comparator phases now write Markdown artifacts and attach them to the
   pre-release state/report. Current-commit Gate F evidence remains operator-run scope.)*

---

## 8. Test Plan

| Test | Proves |
| :--- | :--- |
| Comparator unit fixtures | Warn/fail thresholds, missing baselines, improved runs, schema v1 fallback |
| Hardware mismatch fixtures | Mismatched CPU/memory/storage does not create false release failures |
| Sample aggregation tests | Median/p95/max math is stable and deterministic |
| Tamper tests | Inflated elapsed/peak/spill metrics produce the expected exit code and messages |
| Pre-release dry run | `Test-PreRelease.ps1 -Plan` shows the correct scale and compare phases |

---

## 9. Completion Criteria

- Scenario-specific performance bands are checked in with the baselines.
- A current-commit standard run can be compared with warnings/failures separated.
- Baseline blessing is documented and remains an explicit maintainer action.
- Release notes and performance claims cite the exact baseline artifact and machine class.
