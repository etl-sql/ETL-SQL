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

## Cross-Platform Measurement (closed)

The CI `Studio Performance Budgets` matrix runs the same test on Windows, Linux, and macOS and
uploads one JSON evidence document per runner. All three were green on CI run `33693971737`
(`release/v0.19.0`, 2026-09-02); these are those artifacts, and they close the cross-platform
certification that checked-in ceilings alone could not.

| Platform | Runner | Startup | JS heap | Keystroke p95 | Aggregate/render p95 | Canvas redraw p95 |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: |
| Windows | Windows NT 10.0.26100 x64 | 1,142.28 ms | 3.80 MiB | 13.5 ms | 2.2 ms | 2.5 ms |
| Linux | Unix 6.17.0 x64 | 543.32 ms | 3.80 MiB | 14.2 ms | 1.3 ms | 1.6 ms |
| macOS | Unix 26.5.2 arm64 | 632.16 ms | 3.81 MiB | 15.4 ms | 1.2 ms | 1.1 ms |

Every metric is inside its ceiling on every platform, with the narrowest margin at roughly a
quarter of budget. Two things are worth reading rather than skimming:

- **Startup is the only metric that varies by platform**, and it varies by about 2x: the Windows
  runner takes roughly twice as long as either Unix runner. That is why the Windows ceiling is
  tighter (5,000 ms) than the Unix ones (6,000 ms) and still the closest to being hit. It is a
  runner-profile difference, not a Studio one, but it is the number to watch first when this job
  starts failing.
- **Heap is identical across all three** to within 0.02 MiB, which is what a JavaScript heap
  measurement should look like and a useful check that the metric is measuring Studio rather than
  the host.

### What these numbers do and do not support

- Supported: aggregation and full-canvas redraw p95 stay in **low single-digit milliseconds**
  (1.1-2.5 ms), and keystroke input-to-frame p95 (13.5-15.4 ms) stays **inside a single 16.7 ms
  60 Hz frame** on every platform.
- Not supported, and not to be published: a flat **"~1 ms"** figure - Windows measures 2.2 ms and
  2.5 ms, so the round number is true only on the Unix runners - and **"sustained 60 FPS"**, which
  no measurement here makes. Every metric above is a p95 of one operation repeated in a headless
  runner; none of them observes a sustained display refresh rate over time, and a p95 under the
  frame budget is not the same claim.

## Reproduce

```powershell
.\scripts\Measure-StudioPerformance.ps1 -Configuration Release
```

Use `-NoBuild` only after building the browser test project in the same configuration. To change a
ceiling, retain the failing artifact, repeat the measurement on all three platforms, and document
why the product or runner profile changed. A single noisy run is not sufficient evidence.
