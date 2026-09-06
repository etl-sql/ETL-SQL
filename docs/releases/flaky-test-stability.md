# Flaky Test Stability

This is the canonical record and maintenance policy for timing-sensitive tests. It consolidates
the v0.15.0, v0.17.0, and v0.18.0 tracking notes after the wall-clock wait class was retired in
v0.19.0.

## Policy

Tests must wait for an observable condition, not sleep and hope that work completed. Use
`LoadAwareWait.UntilAsync` from `tests/TestSupport/LoadAwareWait.cs` when an asynchronous condition
needs a bounded wait. Each call names the condition, supplies a baseline budget, and describes its
last observed state. The helper calibrates ThreadPool dispatch once per test process and scales the
baseline by a bounded factor from 1x to 4x.

A timeout reports the condition, elapsed time, baseline and scaled budgets, calibration result,
attempt count, and last state. Set `ETLSQL_WAIT_TIMING_EVIDENCE` to a JSONL path to retain the same
data for successful waits.

Do not add retries around a flaky test. Retries hide intermittent product defects as effectively as
they hide scheduler noise.

## Reference pattern

```csharp
await LoadAwareWait.UntilAsync(
    "job 'daily-sales' to become terminal",
    cancellationToken => client.GetJobAsync("daily-sales", cancellationToken),
    job => job.Status is "Completed" or "Failed",
    TimeSpan.FromSeconds(15),
    pollInterval: TimeSpan.FromMilliseconds(200),
    describe: job => $"status={job.Status}");
```

Use a deliberate bounded observation window only for a negative assertion, TTL/lease behavior, or
an upper bound with a wide discriminating gap. Annotate reviewed exceptions with
`flaky-delay-ok`, `flaky-time-bound-ok`, or `flaky-wait-budget-ok` and a concrete reason.
`scripts/check-flaky-test-delays.mjs` enforces these rules in CI, including bare deadline loops and
waiting helpers.

## Closure evidence (v0.19.0)

`scripts/Measure-TestWaitDistribution.ps1` repeated the historically sensitive Orchestrator and
Portal slices three times under two concurrent CPU-load workers on 2026-08-09. All six lane runs
passed. The raw JSONL, per-run logs, and generated JSON/Markdown summary are reproducible build
artifacts under `artifacts/test-wait-evidence/<timestamp>` and are intentionally not committed.

| Condition family | Samples | p95 | Maximum | Baseline budget |
| :--- | ---: | ---: | ---: | ---: |
| Scheduler mock invocation | 27 | 64 ms | 82 ms | 10 s |
| Portal startup validators | 6 | 0.3 ms | 0.3 ms | 15 s |
| Portal audit/token maintenance | 6 | 1.05 s | 1.05 s | 15 s |
| Subscription completion history | 3 | 532 ms | 532 ms | 15 s |
| Portal execution terminal state | 6 | 1.04 s | 1.04 s | 60 s |
| Process exit observation | 3 | 0.4 ms | 0.4 ms | 10 s |

The baselines therefore remain unchanged. They have substantial measured headroom, while the
bounded calibration protects a genuinely saturated ThreadPool without making ordinary failures
slow.

The first loaded run also exposed a real harness isolation bug: `SchedulerServiceTests` used the
machine-wide production throttle database. Each test instance now receives a private temporary
SQLite throttle store. The subscription trigger tests were also moved onto signed Admin identity
assertions so they exercise the production per-object authorization boundary instead of the legacy
API-key principal.

## Hosted-service lane decision

`HostedServiceLaneTests` starts the complete Portal `IHostedService` pipeline; ordinary Portal tests
remove that pipeline. It now carries `Category=HostedServices` and runs in the dedicated
`portal-hosted` process. The `portal`, `full`, and `release` lanes still run it, but in a separate
invocation after the ordinary Portal suite. This preserves coverage while removing unrelated xUnit
class load and shared background-service state from its startup/shutdown observations.

## History and outcomes

- **v0.15.0:** Eight scheduler tests used a fixed 500 ms delay before positive assertions. They
  moved to condition polling. A cluster-lock failure was separately diagnosed as a production
  lock-release defect and fixed with bounded release retry and ownership verification.
- **v0.17.0:** Process timeout/cancellation tests widened the discriminating gap to a 120-second
  child versus a 60-second upper bound. The concurrent Portal snapshot case remained an observable
  SQLite connection-lifetime investigation rather than receiving a retry.
- **v0.18.0:** The subscription failure scenario and missing dataset-key startup validator exceeded
  fixed wall-clock expectations only under full-suite load. Both now use the shared observable wait;
  the hosted-service suite also moved to its own process.
- **Browser fixture startup:** A separate alternating whole-assembly failure was traced to the
  machine-wide security-event SQLite outbox. Browser factories now use isolated outbox paths and a
  shared collection fixture, preserving a useful reminder that cross-process local database paths
  are part of test and deployment isolation.

- **v0.19.0 — the browser lane fails as a whole under gate load, and says nothing useful when it
  does.** Inside the release gate, after roughly forty minutes of other lanes, the lane reports
  **178 failed / 53 passed of 231**, every failure identical: `The server has not been started or no
  web application was configured`, thrown from `PortalBrowserFixture.InitializeAsync` line 28. That
  is *one* host that failed to start, reported 178 times, and it names none of the real conditions —
  the same shape that has already produced a wrong root cause more than once.

  The lane's content is sound. Run on its own it is **231/231** in Debug and **230/231** in Release,
  and the single Release outlier —
  `RoleJourneyTests.EachRole_IsOfferedExactlyTheSurfacesItCanUse`, a page error caught by
  `Assert.Empty(session.PageErrors)` — passes **5/5** when re-run alone. Configuration is not the
  variable: isolated Release passes. Free memory is a contributor but not the whole story; the
  cascade reproduced with 12.6 GB free after Docker's WSL VM was shut down.

  **The reusable lesson is about the instrument, not the tests.** A fixture whose failure is
  broadcast to every test in the assembly, stripped of its cause, converts one diagnosable problem
  into 178 undiagnosable ones and invites exactly the wrong root cause. Repairing that — surfacing
  the real startup exception, and not running every test class against one shared Portal, admin
  account and sign-in gate — is slice 3 of *Code Stability* in [`ROADMAP.md`](../../ROADMAP.md).
  Until then, treat a whole-assembly `PortalBrowserFixture` failure as an instrument reading and
  re-run the lane in isolation before believing anything it says about the product.

Future timing incidents belong in this document only when they add a reusable lesson. Per-run
diagnostics belong in generated evidence, not a new release-specific tracking file.
