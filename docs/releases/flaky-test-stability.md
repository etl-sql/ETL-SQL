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
- **v0.21.0 — Portal journey dependencies:** The Orchestrator page's new activity requests exposed
  a missing backend in the shared journey fixture: console checks received 503 responses from an
  unconfigured proxy target. `PortalJourneyFixture` now starts an isolated real Orchestrator and
  routes the Portal's authenticated client to its HTTP test server. It disables scheduler loops,
  starts the service before the Portal, and disposes the Portal before the service. Console checks
  retain their assertions and use the existing network-idle wait without an extra fixed sleep.

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

- **v0.19.0 — a connector's own timeout option is not a wait.**
  `KafkaIntegrationTests.KafkaDataSource_ProduceAndConsume_RoundTrip_Success` produced two rows,
  read once with the data source's `TIMEOUT_MS = 3000`, and asserted `NotEmpty` on the result. On a
  loaded CI runner the consumer's group join, partition assignment and offset reset can consume that
  entire window, so the single read returned nothing and the test failed in *exactly* three seconds
  — the timeout, not the broker. It passed everywhere else, including a full local Docker
  integration run.

  The lesson is that a timeout **parameter passed to the code under test** reads like a wait and is
  not one: it bounds one attempt, and the assertion still runs against whatever that attempt
  happened to see. The read is now inside `LoadAwareWait.UntilAsync`, waiting for the observable
  condition — both produced rows readable — which is not the same as retrying a flaky assertion: a
  read returning one row of two is polled again, and a broker that never delivers still fails, with
  the last observed state named. Worth checking wherever a test hands a `TIMEOUT_MS`, `poll`, or
  `wait` option to a connector and then asserts on a single call.

- **v0.20.0 — the wait-shape audit, and what it found.** All four v0.19.0 shapes were swept across
  the whole test tree, not only the browser lane: `TIMEOUT_MS` handed to code under test, a retry
  catching the wrong exception type, an assertion racing a first health probe, and presence-only
  waits. The first three are closed as classes — each had one instance, each is fixed, and the
  remaining lookalikes are deterministic (a mocked Kafka consumer, an `AlwaysHealthyHandler` stub).

  **The reusable lesson is how to tell a presence-only defect from a lookalike.** Three Studio
  journeys click *apply* and then check a filter value, but wait only for the
  `ETL-SQL-STUDIO-FILTER` marker — the MAPPINGS shape exactly. They are nonetheless correct, because
  `DesignerQueryFilterService.BuildCategorical` returns `null` for empty values, so the apply click
  writes no marker at all and the marker cannot appear before the value lands. A wait is presence-only
  when its predicate names a *container* and some earlier interaction can fill that container alone;
  reading the predicate is not enough to know that, and the answer is in the code that writes it.

  Two outcomes worth keeping. `StudioPaginatedJourneyTests` rewrites the script and then waits on
  `!includes('SOURCE = (SELECT')` without ever having asserted that text was present — a negative
  wait is satisfied by a script that never contained it, so the step would go quiet rather than red
  the day it stopped applying. It now asserts the premise first. And
  `SchedulerServiceTests.LegacyUnboundJob_DoesNotWriteTenantUsage` *looks* vacuous — it waits for
  `LogJobEndAsync` while the `SaveTenantUsageAsync` it forbids runs later, in the attempt's `finally`
  — but removing the tenant guard from `SchedulerService` makes it fail, so `Stop()` drains the
  in-flight attempt and the barrier holds. Mutating the product is what settles these; reading the
  ordering is what raises them.

- **v0.20.0 — the browser lane's Portals are split, and the cascade now reproduces locally.** The
  lane no longer runs every class against one Portal. Five themed collections each own a Portal, and
  therefore their own SQLite databases, script root, seeded administrator and forced first-run
  password change: `studio-authoring`, `studio-surfaces`, `portal-journeys`, `portal-admin` and
  `sandbox-stories`, alongside the existing Portal-less `detail-surface`. This is safe because
  `PortalWebFactory` already gives each instance its own `portal_test_<guid>` directory, so two live
  hosts share no files, and it costs about 21 seconds on a five-minute lane.

  **Containment was verified rather than assumed.** Making `SandboxStoryFixture` throw on startup
  failed 60 tests — all of them in `SandboxStoryTests`, each naming the injected cause — while the
  other four groups ran and reported normally. Before the split the same failure was 178 tests
  reporting a message that named nothing.

  During the initial split the startup race still reproduced locally, including a contained
  failure of all 70 portal-journey tests while the other groups passed. CreateHost returned without
  throwing, then CreateClient found a TestServer whose Application had not yet been assigned.

- **v0.21.0 — wait for each host's own startup signal.** The builder supplied to the browser factory
  is a DeferredHostBuilder. Both built hosts share its completion source: after Kestrel starts,
  Start() on the TestServer host can return before that host has started. PortalBrowserFactory now
  waits for each host's ApplicationStarted signal with a named timeout. Async teardown stops
  Kestrel before deleting the shared test files.

  The repaired browser lane passed 580 tests without skips. After the latest Portal-module changes,
  affected Portal journeys passed 26 tests and sandbox stories passed 69. The ordinary Portal lane
  passed 1,352 tests; its separate hosted-service lane passed six. Commands and TRX evidence belong
  under artifacts/section1; the final release still reruns the lanes against its complete source.

  Scoped connector-registry tests now capture the previous registry before invoking a constructor
  that publishes globally. The scope restores it on disposal and if construction throws. The
  focused dialect/suggestion run passed 30 tests, including both restoration regressions.


- **v0.21.0 — green slices are not a green lane.** The first full Portal and browser runs after a
  schema rename found 21 and 3 failures behind per-slice evidence that was all passing. Three of
  the shapes do not announce themselves as naming errors: a command interceptor matching the old
  `INSERT INTO` text never fires, so its test fails as a 10-second timeout; an assertion over
  captured SQL fails as an empty collection; and a Playwright route glob written for a replaced
  URL stops intercepting, so a failure-state test exercises the success path. After a rename,
  search the tests for raw SQL, interceptor string matches, and route globs before reading a
  timeout as load.

  Two browser journeys each timed out once in a full lane and passed alone. Both were recorded as
  probable load. Both were product defects.
  `AdminSubscriptionsPageTests.Subscriptions_ParametersPagingStatusAndDeletePersistThroughTheNativePage`
  timed out waiting for a page-one row after clicking Previous. Run alone in a loop it failed about
  one time in ten, and a dump of the page showed page one loaded but in an arbitrary order: the
  administrator catalog sorted and searched on `Subscription.Recipients`, which is encrypted at
  rest. The database was ordering ciphertext, so the journey's subscription landed on page two
  whenever its ciphertext sorted last, and a search by recipient could never match. The catalog now
  sorts by report, name, and id and matches recipients after decryption
  (`AdminSubscriptionCatalogTests`).
  `StudioStewardshipJourneyTests.Certifies_TheStewardshipFixLoop` timed out the same way and is now
  explained (2026-10-10): it was a product defect, not load. A governance tab opened while the
  reload after a scan was in flight was drawn as "Loading governance data…" and never redrawn. The
  journey's wait for the scan was satisfied by the previous scan's line, so its click landed in
  that window whenever the scan was quick. The page now redraws when that reload finishes
  (`Governance_ATabOpenedWhileAScanReloadsIsDrawnWhenTheReloadFinishes` holds the read to prove it)
  and the journey waits on its own scan response.

  **The reusable lesson: "once, under load, passes alone" is not a diagnosis.** Neither failure
  was reproduced by rerunning once. Each was found by looping the single test until it failed and
  making the failing wait print what the page held. A failure rate near 1/N for a fixture of N
  rows points at ordering; a wait stuck on a loading banner points at a click inside a read.
  One failure is explained in mechanism but not confirmed. `SubscriptionIntegrationTests` failed in
  two filtered Portal runs on 2026-10-10: two tests in one, ten of sixteen in the other, each after
  about 17 seconds, which is the 15-second wait for a triggered job's history. The failure text was
  filtered out of both runs and the failure did not recur in eight later runs, so there is no
  message to read. The runs were slow (4m34s and 5m14s against 2m39s to 3m20s), but ten 17-second
  timeouts account for that by themselves; slowness is not evidence of load.

  Reading the trigger path found the one way a trigger can answer `202` and leave no history row:
  an Orchestrator node that judged itself overloaded accepted a manual trigger and then dropped
  the run without a word. That is a product defect whatever caused these failures. The node now
  refuses the trigger with `503` (`OverloadedNode_RefusesAManualTriggerInsteadOfDroppingIt`,
  `TriggerOnAnOverloadedNodeIsRefusedAndRecordsNoRun`).

  Two things can make a test host's node judge itself overloaded. The first was a second defect:
  the first CPU sample after start-up spans under a millisecond, where one clock tick of processor
  time on two threads reads as 100% of every core. A probe measured it at 1 to 5 fresh monitors in
  100 with one to four busy threads on 24 cores; it is fixed
  (`Capture_FirstSampleAfterStartUp_DoesNotReadAFewBusyThreadsAsAnOverloadedNode`). It lasts one
  second, and these tests trigger roughly two seconds after their host starts, so it probably did
  not cause these failures. The second is host memory at or above 95%, which the real monitor
  reads from the machine. It would persist across consecutive tests, which fits ten failures in one
  run, and a consumer gate and a second session were using the machine that day. Nothing measured
  it at the time.

  **What a recurrence will now show.** The trigger helper prints the response body, so an
  overloaded node fails the test at the trigger with its reason instead of at a history timeout.
  If that message appears, give `OrchestratorWebFactory` the fixed capacity reading that
  `PortalWebFactory` already has. If a history timeout appears with a `202`, this explanation is
  wrong and the search starts again from that log.

  **A fixed wait around a full preparation is outgrown, not flaky.** The first CI run after 56
  commits (2026-10-11) failed three checks that had passed locally. Two had a known cause. The
  extension's unit test for the offline bundle runs `test-runtime-bundle.mjs`, more than a dozen
  full preparations in child processes, under the default five-second test timeout; it already takes six
  seconds on a fast machine. The watcher check in `test-asset-generation.mjs` allowed fifteen
  seconds for two preparations on a runner where the whole consumer gate took twice as long as it
  does locally. Both waits had been right when there were fewer TypeScript owners to compile. The
  first now has a timeout sized for what it runs; the second measures one preparation and waits ten
  of them.

  The third needed its own message first. `test-service-capacity-smoke.mjs` reported that a
  0.2-second workload against a local stub breached its criteria without saying which. Once it
  printed the breach, the next failure read `p95 3325.01ms > 2000ms` for the Orchestrator step, in
  a run where the consumer gate took 22 minutes against 8 locally. I took that for the runner being
  slow and raised the ceiling. That was wrong, and the next run said so: with latency out of the
  way the same check failed on a missing process-memory sample.

  Both came from one defect in the driver. On Windows it sampled process metrics by running
  `powershell.exe` synchronously. That froze the event loop that sends and times the workload's
  requests for as long as PowerShell took to start, so each sample added its start-up time to a
  request's latency; and when start-up passed the call's five-second limit, the sample was lost.
  Measured locally against the stub, the Orchestrator step reported a p95 of about 300 ms before
  the fix and under 1 ms after it, with the same request to the same stub. Any Windows capacity
  run with process sampling was reporting latencies that included its own instrument. The sampler
  is now asynchronous, the ceiling is back at 2,000 ms, and the smoke run fails if the sampled
  step's p95 against the stub exceeds 100 ms.

  The lesson: a slow runner was the comfortable explanation and it fitted the first symptom. The
  second symptom did not fit it, and a measurement on a fast machine showed the same inflation.

  **Open: the browser lane on the Windows CI runner.** Three CI runs on `release/v0.21.0` gave three
  different results: one sandbox journey timed out after 20 seconds (2026-10-08), all tests passed
  (2026-10-11, `90de4963d`), and two failed (`3c7d89195`). The same commit range passes 929 of 929
  locally and in a clean worktree. In the last run one navigation failed in 54 ms with
  `net::ERR_NO_BUFFER_SPACE`, which is Windows refusing a socket for lack of buffer space or ports,
  and a reader sandbox page never reported ready while its folder tree still said "Loading". Both
  fit one cause, requests that could not be made, but only the first is shown by the log. The run
  took 19 minutes against 13 locally. Not yet known: whether the runner is short of memory or the
  lane is leaving sockets open. No wait was lengthened and no retry was added. The next step is to
  record failed requests and the process's handle and socket counts when a navigation fails.

  Two further runs the same day: one passed all 929, and one failed a single test in teardown.
  `OrchestratorWebFactory.DisposeAsync` deleted its temporary directory while `etlsql.db` was still
  open (`ScheduledJobActivityBrowserTests.TriageUsesRealDurableEvidenceAndReportsPartialProxyReruns`).
  Five runs, four different outcomes, none reproduced locally.

  The teardown failure has a cause. That journey is the only one in its class that triggers a real
  run through the Orchestrator, and a triggered run is a detached task. In the service,
  `OrchestratorHostedService` waits for such tasks at shutdown. The fixture removes every hosted
  service to keep the scheduling loop quiet, which removed the wait as well, so the run could
  still be writing its history when the directory was deleted. The pool was not the problem: every
  connection to that file uses the same connection string, and it was being cleared. The lock only
  exists while the run is inside a store call, so it cannot be staged; the regression test asserts
  the property that prevents it instead
  (`Dispose_WaitsForATriggeredRunBeforeDeletingItsDirectory` fails without the wait, with a run
  still active after disposal). `OrchestratorWebFactory.DisposeAsync` now stops the scheduler
  first, and a failed delete reports how many runs were still active and which files remained.
  The lesson: removing hosted services from a test host also removes their shutdown work.

  **Open: `FtpIntegrationTests.ValidCredentials_ListRoot_Succeeds` in the Docker lane.** It failed in
  2 ms with "Failed to connect to host" in two runs on 2026-10-11 and passed in the two before
  them. A 2 ms refusal means nothing was listening on the mapped port when the test ran. The
  container's readiness check was not examined.


Future timing incidents belong in this document only when they add a reusable lesson. Per-run
diagnostics belong in generated evidence, not a new release-specific tracking file.
