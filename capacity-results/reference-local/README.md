# Local Reference Capacity Baseline

This directory contains a measured developer-workstation starter baseline for the Report Portal and
Orchestrator. It is a reproducible lower-bound reference, not a universal production capacity claim.

## Reference Environment

- Measured on June 4, 2026, using a Release build on Windows 10.0.26200 x64.
- Intel Core Ultra 9 275HX, 24 logical CPUs, 33,690,271,744 bytes RAM, local SSD.
- Portal and Orchestrator ran as separate local processes with separate SQLite databases.
- Portal used in-process report execution with `MaxConcurrentReportExecutions=4`.
- Orchestrator used in-process job execution with `MaxConcurrentJobs=4`.
- The Portal report was a deterministic one-row table report. The Orchestrator job was `SELECT 1`.
- Each step ran for 15 seconds. The Orchestrator workload used pacing so trigger traffic represented
  a deliberate jobs-per-hour rate instead of an unbounded HTTP ingestion flood.

The sanitized workload is [`workload.sanitized.json`](workload.sanitized.json). The generated report
is under [`baseline/`](baseline/).

## Results And Guidance

Portal produced no errors or SQLite contention through 120 active workers. Throughput peaked near
37,000 requests/minute at 5-10 workers, then flattened while tail latency increased. At 20 workers,
p95 was 156 ms and p99 was 795 ms. At 40 workers, p99 rose to 1.73 seconds.

Use **20 simultaneously active Portal users** as the conservative starter recommendation for this
class of host and this lightweight mixed workload. This is not a named-user limit. Administrators
should run representative reports, exports, datasets, and downstream connectors before increasing it.

Orchestrator first breached the configured queue threshold at 80 workers: 388 no-op jobs were
triggered in 15 seconds and queued work reached 154. At 40 workers, 247 no-op jobs were triggered in
15 seconds, equivalent to approximately 59,280 jobs/hour, with no sampled queue.

Use **approximately 47,000 lightweight jobs/hour** as the conservative starter recommendation for
this host and configuration, applying a 20% operating margin below the highest no-queue step. Real
ETL, connector I/O, retries, file work, and process-spawning jobs will reduce that number, often
substantially.

After the final load step, `/metrics` reported `active_jobs=0`, `queued_jobs=0`, `max_jobs=4`, and
`available_slots=4`. No SQLite lock or busy errors were observed.

## Reproducing

1. Start isolated Portal and Orchestrator services with the configuration above.
2. Set the test-only `CAPACITY_*` environment variables required by `provision-reference.mjs`,
   including `CAPACITY_INITIAL_ADMIN_PASSWORD` for the fresh Portal bootstrap account.
3. Run the provisioner. It verifies that the report execution produces a readable snapshot manifest
   and CSV export before creating the scheduled job.
4. Copy `workload.sanitized.json` to a secret-bearing ignored local file, replace the redacted values,
   and run:

```powershell
node .\scripts\test-service-capacity.mjs `
  --config .\capacity-results\reference-local\workload.local.json `
  --out-dir .\capacity-results\reference-local\baseline
```

## Limitations

- The Portal baseline uses in-process report execution. Remote Orchestrator report execution is
  verified separately because its manifest transport and network path have different performance
  characteristics.
- The sample report and job are intentionally small. This baseline does not replace workload-specific
  testing for large reports, exports, connector latency, retries, or process-spawning mode.
- Fifteen-second steps identify an initial boundary. Production certification should use longer
  sustained runs and OS-level CPU, memory, GC, and disk monitoring.
