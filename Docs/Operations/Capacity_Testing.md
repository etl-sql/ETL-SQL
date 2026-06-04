# Portal and Orchestrator Capacity Testing

Use `scripts/test-service-capacity.mjs` to measure Portal-user and Orchestrator-job capacity against
an isolated deployment. Correctness lanes prove behavior; this harness measures throughput,
latency, saturation, and operational visibility.

## Reference Environment

Every measured run must record:

- CPU model and logical CPU count
- RAM
- disk type and database placement
- operating system and architecture
- .NET version
- deployment mode: in-process, Docker, or installed services
- Portal `Resources:MaxConcurrentReportExecutions`
- Orchestrator `Jobs:MaxConcurrentJobs` and process-spawning mode
- sample report, dataset, and script sizes
- service log locations and any external monitoring used for CPU, GC, disk I/O, and working set

The harness records host basics and service endpoint samples. Use OS monitoring or `dotnet-counters`
alongside measured runs for target-process CPU, GC, working set, and disk I/O.

## Running A Test

1. Copy `capacity-results/workload.example.json` to an ignored local file.
2. Replace URLs, credentials, API key, resource IDs, environment notes, steps, and breach criteria.
3. Provision representative reports, datasets, users, and scheduled jobs in a non-production database.
4. Include setup and cleanup API requests in the workload file when the scenario can safely create its own data.
5. Run the harness:

```powershell
node .\scripts\test-service-capacity.mjs --config .\capacity-results\workload.local.json
```

Validate a workload file without sending requests:

```powershell
node .\scripts\test-service-capacity.mjs --config .\capacity-results\workload.example.json --validate-only
```

The harness warms each service, runs each concurrency step for a fixed duration, samples configured
metrics endpoints, detects SQLite lock/busy errors, applies breach criteria, and writes JSON and
Markdown reports under `capacity-results/`.

Use `thinkTimeMs` on a service or individual workload request to model a deliberate request rate.
Request-level values override the service default. This is especially important for scheduled-job
triggers: an unpaced worker measures HTTP trigger ingestion, not a defensible jobs-per-hour workload.

## Workload Profiles

Portal runs should cover:

- cache-friendly folder, catalog, report, snapshot, and dataset reads
- cache-cold report execution and refresh
- CSV, XLSX, and PDF export-heavy traffic
- Admin and Publisher writes such as favorites, saved views, alerts, subscriptions, audit views, and metrics
- mixed read/write traffic using Admin, Publisher, and Viewer credentials

Orchestrator runs should cover:

- lightweight no-op jobs
- normal 10K-row temp-table jobs for default operator sizing
- 50K-row and 100K-row temp-table jobs for upper starter and heavier validation tiers
- medium and long ETL-SQL scripts
- file/report export jobs
- mocked connector-I/O jobs
- retry and controlled-failure jobs
- `PARALLEL` scripts
- trigger bursts, dense schedules, varied retry rates, and both process-spawning modes

For a checked-in developer-workstation starter baseline, see
[`capacity-results/reference-local/README.md`](../../capacity-results/reference-local/README.md).

Always label jobs/hour figures with the row profile used. A no-op `SELECT 1` job measures scheduler
and trigger overhead only. It should not be presented as the normal ETL jobs/hour capacity.

For hardware planning, publish at least one row-volume table alongside every jobs/hour number:

| Row profile | What it represents |
| ---: | :--- |
| No-op / 1 row | Scheduler and trigger overhead only |
| 10K rows | Default normal ETL sizing baseline |
| 50K rows | Upper starter tier |
| 100K rows | Heavier validation tier |

Convert observed job duration into a rough ceiling with:

```text
jobs/hour = (3600 / seconds_per_job) * MaxConcurrentJobs
starter_guidance = jobs/hour * 0.8
```

Then validate the estimate with the full harness and watch queue depth, SQLite contention, CPU,
memory, disk I/O, and history-query responsiveness.

## Stepped Load And Breaches

Start with an idle/warm baseline, increase concurrency in fixed steps, and hold each step long enough
to stabilize. The first sustained breach is the capacity boundary. Recommended capacity should stay
below that boundary with an operating margin.

Define breach criteria before the run. Typical breaches include:

- sustained error rate above 1%
- p95 latency above the workflow target
- queue depth that does not drain after load stops
- SQLite lock or busy errors
- worker starvation or missed schedule windows
- CPU, working set, GC, or disk saturation
- health or metrics endpoints becoming unavailable or misleading

## Comparing Runs

```powershell
node .\scripts\compare-capacity-results.mjs `
  .\capacity-results\baseline\capacity-report.json `
  .\capacity-results\current\capacity-report.json `
  15
```

The comparison fails when p95 latency regresses, throughput drops, or error rate increases beyond the
configured threshold.

## Administrator Sizing Guidance

Do not publish universal capacity numbers without measured reports. Use the first sustained breach and
apply a safety margin of at least 20% for a starter recommendation.

Primary tuning knobs:

- Increase `Resources:MaxConcurrentReportExecutions` only when Portal CPU, memory, and downstream data sources have headroom.
- Increase `Jobs:MaxConcurrentJobs` only when queue depth grows and Orchestrator CPU, memory, disk, and SQLite remain healthy.
- Place Portal and Orchestrator SQLite databases on low-latency local storage.
- Split Portal and Orchestrator onto separate hosts when interactive Portal latency is affected by job bursts, export work, or SQLite contention.

Warning signs include rising p95/p99 latency, queues that do not drain, recurring SQLite lock errors,
failed work without useful history, increasing GC pressure, and health endpoints that stop reflecting
active or queued work.
