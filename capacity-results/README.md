# Capacity Test Results

Store measured Portal and Orchestrator capacity reports here in dated subdirectories.

Do not treat `workload.example.json` as a baseline. Copy it, replace credentials and resource IDs,
record the reference environment, and run the harness against an isolated non-production deployment.
The workload templates under [`workloads/`](workloads/) cover cache-cold Portal execution and
exports, representative Orchestrator row-volume jobs, retry/failure jobs, mocked I/O, `PARALLEL`,
schedule density, process-spawning comparisons, and the PostgreSQL HA sustained-load
profile.

```powershell
node .\scripts\test-service-capacity.mjs --config .\capacity-results\workload.local.json
node .\scripts\test-capacity-workload-configs.mjs
node .\scripts\compare-capacity-results.mjs `
  .\capacity-results\baseline\capacity-report.json `
  .\capacity-results\current\capacity-report.json
```

For PostgreSQL HA soak runs, first generate a local topology, then materialize the sustained workload from
that run so generated API keys stay outside source control:

```powershell
.\scripts\New-PostgresHaSoakTopology.ps1 -RunId ha-soak-local -Start
.\scripts\New-PostgresHaCapacityWorkload.ps1 -TopologyRunRoot .\.ha-soak-runs\ha-soak-local -AdminPassword <portal-admin-password>
.\scripts\New-HaSoakRunbook.ps1 -TopologyRunRoot .\.ha-soak-runs\ha-soak-local
node .\scripts\test-service-capacity.mjs --config .\.ha-soak-runs\ha-soak-local\postgres-ha-sustained.workload.local.json --out-dir .\certification-results\postgres-ha-soak\ha-soak-local
.\scripts\Export-PostgresHaMetricsSnapshot.ps1 -TopologyRunRoot .\.ha-soak-runs\ha-soak-local -OutputPath .\certification-results\postgres-ha-soak\ha-soak-local\postgres-ha-metrics.json
.\scripts\Export-HaSoakDiagnostics.ps1 -TopologyRunRoot .\.ha-soak-runs\ha-soak-local
```

Checked-in baselines should include the JSON report, Markdown report, workload configuration with
secrets removed, PostgreSQL metrics snapshot, machine specifications, service configuration, and a
short interpretation of the first sustained breach and recommended operating margin.

The checked-in developer-workstation starter baseline is documented in
[`reference-local/README.md`](reference-local/README.md).

For administrator-facing server sizing guidance, see
[`docs/architecture/decisions/Capacity_Planning.md`](../docs/architecture/decisions/Capacity_Planning.md).

When publishing Orchestrator job capacity, record the row profile used. A `SELECT 1` no-op job is
useful for scheduler and trigger lower bounds, but normal operator guidance should start with a
10K-row workload and then test 50K/100K tiers as heavier validation profiles.

The local reference baseline includes an initial row-volume sizing table in
[`reference-local/README.md`](reference-local/README.md). Treat that table as starter hardware
planning guidance until the same row profiles are run through the full service-capacity harness.
