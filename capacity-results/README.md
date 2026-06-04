# Capacity Test Results

Store measured Portal and Orchestrator capacity reports here in dated subdirectories.

Do not treat `workload.example.json` as a baseline. Copy it, replace credentials and resource IDs,
record the reference environment, and run the harness against an isolated non-production deployment.

```powershell
node .\scripts\test-service-capacity.mjs --config .\capacity-results\workload.local.json
node .\scripts\compare-capacity-results.mjs `
  .\capacity-results\baseline\capacity-report.json `
  .\capacity-results\current\capacity-report.json
```

Checked-in baselines should include the JSON report, Markdown report, workload configuration with
secrets removed, machine specifications, service configuration, and a short interpretation of the
first sustained breach and recommended operating margin.

The checked-in developer-workstation starter baseline is documented in
[`reference-local/README.md`](reference-local/README.md).
