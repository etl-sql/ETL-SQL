# Portal and Orchestrator Capacity Planning

Use this guide to turn an expected user base and job schedule into a starter server plan for
Report Portal and Orchestrator. The numbers below are planning anchors, not universal limits.
Validate the final shape on the target hardware with the capacity test harness in
[Capacity_Testing.md](Capacity_Testing.md).

## What Drives Capacity

Portal capacity is driven by:

- named users and the percentage active at the same time
- report catalog, folder, snapshot, and dataset reads
- report executions and refreshes
- CSV, XLSX, and PDF exports
- Admin and Publisher writes such as saved views, alerts, subscriptions, favorites, and audit views
- report complexity, dataset size, and downstream connector latency

Orchestrator capacity is driven by:

- jobs/hour during the busiest window
- rows processed per normal job
- job duration and script complexity
- connector I/O and downstream database or file-server latency
- retry volume and controlled failures
- `PARALLEL` work inside scripts
- `Jobs:MaxConcurrentJobs` and whether process spawning is enabled

## Planning Terms

Named users are total users who can log in. Active concurrent users are the users doing work at the
same time. For a normal internal reporting system, a starting estimate is usually 5% to 20% of named
users active at once, then adjust upward for shift handoffs, operational dashboards, end-of-month
cycles, or report-heavy teams.

Jobs/hour must always be tied to a row profile. A no-op job is useful for proving scheduler overhead,
but it does not represent ETL throughput. Use 10K rows as the default normal ETL sizing baseline,
then test 50K and 100K rows when those are plausible peak workloads.

## Reference Baseline

The checked-in local baseline was measured on a high-end developer workstation with Portal and
Orchestrator running as separate local processes, separate SQLite databases, and
`Jobs:MaxConcurrentJobs=4`. See
[`capacity-results/reference-local/README.md`](../../capacity-results/reference-local/README.md) for
the exact machine and run notes.

| Workload | Observed starter guidance |
| :--- | :--- |
| Portal lightweight mixed workload | 20 simultaneously active users is a conservative starter recommendation |
| Orchestrator no-op job | About 47,000 jobs/hour, scheduler and trigger lower bound only |
| Orchestrator 10K-row job | About 1,200 to 1,300 jobs/hour with 20% margin at 4 workers |
| Orchestrator 50K-row job | About 420 jobs/hour with 20% margin at 4 workers |
| Orchestrator 100K-row job | About 220 jobs/hour with 20% margin at 4 workers |

Use the 10K-row line as the default normal-workload planning baseline. Use no-op capacity only to
reason about trigger overhead and scheduler plumbing.

## Starter Server Shapes

These are first-pass server shapes for planning conversations. They should be verified with the
target reports, scripts, users, storage, and service settings before production rollout.

| Shape | Typical use | Starter host plan | Planning boundary |
| :--- | :--- | :--- | :--- |
| Small shared host | Pilot, development, or small internal team | One host, 8 vCPU, 32 GB RAM, low-latency SSD/NVMe, separate Portal and Orchestrator SQLite files | Up to about 20 active Portal users and about 1,000 10K-row jobs/hour |
| Standard split host | Normal shared reporting and scheduled job service | Portal host: 8 vCPU, 32 GB RAM. Orchestrator host: 8 to 16 vCPU, 32 to 64 GB RAM. Low-latency SSD/NVMe on both. | More than 20 active Portal users, job bursts near 1,000 10K-row jobs/hour, or visible interference between Portal and jobs |
| Heavy or enterprise | Many active users, heavy exports, high job volume, strict service targets | Separate Portal, Orchestrator, and database/storage planning. Start at 16+ vCPU and 64+ GB RAM per application host, then measure. | 75+ active Portal users, 5,000+ 10K-row jobs/hour, frequent 50K/100K jobs, HA, or formal SLA requirements |

The standard and heavy rows are not promises that one larger server will scale linearly. Connector
latency, disk latency, SQLite write pressure, report exports, and long-running scripts can become the
real bottleneck before CPU does.

## Estimating Jobs Per Hour

Start with a measured or expected duration for the row profile:

```text
jobs/hour = (3600 / seconds_per_job) * MaxConcurrentJobs * 0.8
required_workers = ceiling(target_jobs_per_hour * seconds_per_job / 3600 / 0.8)
```

The `0.8` factor keeps a 20% operating margin. Do not remove the margin for production planning.

Examples:

- 300 jobs/hour at 10K rows is below the local 4-worker starter guidance. A small shared host may be
  reasonable if Portal usage is also light.
- 1,500 jobs/hour at 10K rows exceeds the local 4-worker starter guidance. Plan for a split
  Orchestrator host and validate higher `Jobs:MaxConcurrentJobs` on target hardware.
- 2,000 jobs/hour at 50K rows is far above the local 4-worker 50K-row guidance. Treat this as a
  measured-capacity project, not a default installation.

## When To Split Portal And Orchestrator

Run both services on separate hosts when any of these appear:

- Portal p95 or p99 latency rises during job bursts
- report refreshes or exports make catalog browsing slow
- Orchestrator queue depth grows and does not drain after the busy window
- SQLite lock or busy errors appear
- CPU stays above 70% to 80% during normal peaks
- memory pressure or GC activity rises during exports or job execution
- disk latency or disk queue length rises during snapshots, exports, or history writes

For a shared host, keep separate SQLite database files for Portal and Orchestrator and place them on
low-latency local storage. Avoid network shares for active SQLite database files.

## Database And Storage Guidance

SQLite is appropriate for starter deployments when the database files are on fast local storage and
write pressure is moderate. Move to a broader database and storage plan when the deployment needs:

- multiple application nodes writing to the same data store
- high availability or formal database failover
- heavy concurrent subscription, alert, audit, and job-history writes
- long retention with large history tables
- centralized backup, restore, and retention operations

Backups should cover Portal database files, Orchestrator database files, report snapshots, datasets,
script roots, configuration files, certificates, keys, and service logs.

## What To Give A Server Administrator

Before requesting servers, provide:

- named Portal users and expected active concurrent users
- peak report views/hour, report refreshes/hour, and exports/hour
- expected jobs/hour by row profile: no-op, 10K, 50K, and 100K
- largest normal job and largest rare job
- required schedule windows and queue-drain expectations
- expected retention for snapshots, datasets, audit history, and job history
- backup and restore requirements
- authentication, SMTP, TLS, certificate, and reverse-proxy requirements
- expected connector targets and whether they are local, LAN, WAN, or cloud services

## Validation Checklist

Before calling a deployment sized, run the technical procedure in
[Capacity_Testing.md](Capacity_Testing.md) and record:

- machine specifications and service settings
- Portal p95 and p99 latency under the expected active-user load
- report refresh and export latency under load
- Orchestrator jobs/hour by row profile
- queue depth during and after the busy window
- SQLite lock or busy errors
- CPU, memory, GC, disk latency, and disk queue behavior
- first sustained breach and the recommended operating margin

If the measured result differs from this guide, trust the measured result for that environment.

## Historical Capacity Report

The native `Portal:AdminServices:CapacityReport` digest summarizes the short lookback window and the
retained daily rollups. Use the short-window section to spot immediate pressure, and use the
historical trend section to decide whether the deployment is growing toward a capacity boundary.

The report distinguishes the signals ETL-SQL can measure directly:

- **CPU** - sustained host or process CPU saturation points toward scale-up or scale-out.
- **Memory** - high host memory or job peak memory points toward larger hosts, lower concurrency,
  smaller batches, or report/export tuning.
- **Storage** - state/spill disk floors and forecasts point toward storage expansion, retention
  changes, or repartitioning spill-heavy workloads.
- **Workload** - daily run count, failures, rows, and busiest day show schedule pressure and growth.
- **Queue wait vs. run duration** - high queue wait with comparatively normal run duration points
  toward execution-slot saturation or schedule bursts; high run duration with low queue wait points
  toward slow report logic, downstream connectors, databases, exports, or storage.
- **Hourly Portal pressure** - inferred queued/active overlap from persisted execution lifecycle rows
  shows whether queue bursts coincided with active slots at the configured cap. Queued overlap below
  the global cap usually means per-user/per-group limits, node-capacity admission, or burst timing
  need review before raising the global cap.

Connector, database, and concurrency bottlenecks need correlation with external telemetry and queue
history. If job duration rises while CPU, memory, and disk remain healthy, inspect downstream
databases, APIs, file shares, and connector latency before increasing ETL-SQL concurrency. If queue
depth or queue age rises while hosts have headroom, adjust schedule distribution or concurrency caps
and validate with the capacity harness.

## Interpreting Capacity Signals

Use the capacity report as a decision aid, not as an automatic scaler. The first question is which
resource is saturated while the workload is slow or queued.

| Signal pattern | Likely bottleneck | First action |
| :--- | :--- | :--- |
| CPU max or p95 at 80%+, memory and disk healthy, queue wait rising | CPU or worker-slot saturation | Scale up CPU, scale out Portal/Orchestrator nodes where supported, or lower overlap between heavy jobs |
| Memory max at 80%+, high job peak memory, repeated large exports or snapshots | Memory pressure | Increase RAM, reduce concurrent exports/refreshes, lower batch sizes, or split Portal and Orchestrator |
| State or spill disk floor near the report threshold, forecast trending downward | Storage capacity or spill-heavy workload | Increase storage, shorten retention, move spill/state to faster/larger volumes, or repartition spill-heavy jobs |
| Queue wait high, active slots at cap, run duration normal | Concurrency cap or schedule burst | Spread schedules, raise concurrency only after CPU/memory/disk are healthy, or add nodes where topology supports it |
| Run duration high, queue wait low, host resources healthy | Connector, database, API, file share, or report logic | Inspect downstream telemetry, query plans, connector latency, export size, and report script complexity |
| Queue overlap below global cap but users still wait | Per-user/per-group cap, node-capacity admission, or burst timing | Review fairness caps and node capacity limits before raising the global execution cap |
| Failure rate rises with latency but host resources are healthy | External dependency or script quality | Inspect provider errors, retry behavior, source-system health, and recent script changes |

## Scale Decision Examples

**Scale up** when one node is consistently CPU or memory bound and the deployment is still a
single-node or split-host topology. Example: Portal p95 queue wait is 12 seconds, active slots are at
cap, CPU p95 is 85%, and memory is stable. Add CPU or move Portal/Orchestrator to separate hosts
before increasing `Resources:MaxConcurrentReportExecutions`.

**Scale out** when HA is already configured, multiple nodes can share state safely, and pressure is
node-local rather than database/storage-bound. Example: two Portal nodes show active slots at cap,
CPU and memory are high, PostgreSQL and shared storage remain healthy, and sticky-session routing is
working. Add another Portal node and validate that load distribution improves without increasing
database lock or storage latency.

**Repartition workloads** when one script, report, or schedule family dominates the busiest hour.
Example: the report shows one refresh group producing most rows and peak memory. Split the job by
date, region, tenant, or source system; stage into smaller `#temp` batches; or run the heavy refresh
outside the interactive reporting peak.

**Adjust schedules** when queue pressure is bursty but total daily work is reasonable. Example: the
busiest queued hour is 08:00Z with active slots at cap, while the rest of the day is idle. Spread
subscriptions, refreshes, and orchestrator jobs across the hour or move non-urgent jobs outside the
business-start window.

**Do not scale ETL-SQL first** when run duration rises while host CPU, memory, storage, and queue
wait remain healthy. That pattern usually means a downstream database, API, file share, cloud
service, or report query is slow. Raising concurrency can amplify the source-system bottleneck.

## Measured History vs. Synthetic Estimates

The reference baseline and formulas are starter estimates. Use them before a production history
exists, then replace them with measured workload history as soon as the deployment has enough data.

Measured history is required before making claims about:

- production jobs/hour for real row profiles
- month-end, shift-handoff, subscription, or dashboard peak windows
- connector and database bottlenecks
- spill-heavy transformations and large exports
- HA node count and load-balancer behavior
- retention impact on state, snapshot, dataset, audit, and outbox storage

Synthetic estimates are acceptable only for initial server requests, lab comparisons, and deciding
which capacity harness profiles to run. Production sizing should cite the capacity report, harness
results, host telemetry, and downstream system evidence together.
