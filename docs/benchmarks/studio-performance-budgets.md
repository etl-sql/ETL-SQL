# Studio Performance Budgets

This benchmark measures the canonical Studio UI-sandbox fixture in headless Chromium. It replaces
the earlier estimates for Kestrel-only startup, process working set, keystroke latency, aggregation,
and sustained frame rate with metrics that the repository can reproduce on every supported desktop
platform.

## Fixture and Metrics

The `studio` story opens three documents and renders the two-visual dashboard backed by its normal
CodeMirror and designer modules. The test records:

- **Startup** — navigation start through a mounted Studio workbench and ready editor.
- **JavaScript heap** — Chromium's `Runtime.getHeapUsage` value after an explicit garbage collection.
- **Keystroke p95** — 24 real CodeMirror edits measured from `beforeinput` through the next animation frame.
- **Aggregation p95** — 40 forced-layout BAR renders over 250 rows, 25 categories, and four series.
- **Canvas redraw p95** — 30 full two-card snapshot redraws including forced layout.

The heap metric deliberately does not claim to represent Kestrel, browser, or operating-system
working set. The redraw metric checks whether Studio's own work fits inside a 16.7 ms 60 Hz frame
budget; it does not claim that a headless CI runner sustains a particular display refresh rate.

## Checked-in Ceilings

| Platform | Startup | JS heap | Keystroke p95 | Aggregate/render p95 | Canvas redraw p95 |
| :--- | ---: | ---: | ---: | ---: | ---: |
| Windows | 5,000 ms | 64 MiB | 50 ms | 16.7 ms | 16.7 ms |
| Linux | 6,000 ms | 64 MiB | 50 ms | 16.7 ms | 16.7 ms |
| macOS | 6,000 ms | 72 MiB | 50 ms | 16.7 ms | 16.7 ms |

The machine-readable source is
[`studio-performance-budgets.json`](studio-performance-budgets.json). These are deliberately coarse
regression ceilings with room for shared GitHub runners, not targets inferred from a single fast
machine.

## Initial Windows Measurement

Measured on Windows 11 x64 in a Release build on 2026-08-31 using the command below:

| Startup | JS heap | Keystroke p95 | Aggregate/render p95 | Canvas redraw p95 |
| ---: | ---: | ---: | ---: | ---: |
| 2,367.22 ms | 3.67 MiB | 16.00 ms | 1.00 ms | 1.00 ms |

The CI `Studio Performance Budgets` matrix runs the same test on Windows, Linux, and macOS and
uploads one JSON evidence document per runner. The TODO certification remains open until the first
green Linux and macOS artifacts are reviewed; checked-in ceilings alone are not cross-platform
measurement evidence.

## Reproduce

```powershell
.\scripts\Measure-StudioPerformance.ps1 -Configuration Release
```

Use `-NoBuild` only after building the browser test project in the same configuration. To change a
ceiling, retain the failing artifact, repeat the measurement on all three platforms, and document
why the product or runner profile changed. A single noisy run is not sufficient evidence.
