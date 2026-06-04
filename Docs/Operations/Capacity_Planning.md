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
