# Certification Results

This directory stores curated certification evidence for ETL-SQL scale, HA, spill, columnar, and enterprise-hardening gates. These files are intentionally source-controlled when they represent a reproducible baseline, release certification result, or decision record that future work should compare against.

## Source control policy

Include results here when they are:

- **Curated evidence** - JSON or Markdown summaries from certification scripts, benchmark gates, fault-injection plans, soak reports, and capacity reports.
- **Reproducible baselines** - results tied to checked-in scripts, scenarios, workload descriptions, or documented machine/service configuration.
- **Small enough to review** - summary reports, reduced metrics, and representative benchmark outputs that can be diffed in pull requests.
- **Sanitized** - no raw secrets, connection strings, access tokens, private hostnames, or operator-only credentials.

Do not check in local scratch output, transient console captures, unsanitized logs, machine-specific temp files, or oversized raw dumps unless they are deliberately promoted as evidence and documented in the relevant run folder.

## Layout

| Path | Purpose |
| :--- | :--- |
| `cert-report.json` / `cert-report.md` | Current aggregate certification summary. |
| `baseline-smoke.json` / `baseline-standard.json` | Hosted `ubuntu-latest` scale baselines (W6). CI fails on correctness, work-counter or allocation differences against them. |
| `*-scenarios.json` / `*-matrix.json` | Scenario definitions used by certification scripts. |
| `columnar-crossover-benchmarks/` | Columnar crossover benchmark evidence. |
| `ha-fault-injection/` | Fault-injection plans, cleanup invariants, and per-fault outcomes. |
| `ha-large-job-soak/` | Large-job HA soak plans and outcomes. |
| `postgres-ha-soak/` | PostgreSQL HA service-capacity reports and metrics snapshots. |
| `spill-alloc-*` | Spill allocation budgets and measured profiles. The 10M-row budget is a hosted capture. |

## Producing new evidence

Use the certification and capacity scripts under [`../scripts`](../scripts) and keep generated credentials outside this directory. When a run becomes a product baseline, add a short Markdown interpretation beside the JSON so reviewers can see what changed and why it matters.

### Performance baselines (W6)

The scale baselines and the 10M-row spill budget come from hosted runners, not a workstation
([W6 design note](../docs/releases/v0.23.0-w6-performance-gates-design.md)). Against them:

- Correctness (failed scenario, rows, checksum) and work counters (spill bytes, extents, partition
  passes, plan counts) must match exactly; allocated MB may grow by at most 5%.
- Elapsed time, throughput, GC pause and peak working set are reported only. Elapsed time is gated
  by the paired A/B *Paired timing* jobs in Release Evidence (10% against the previous release,
  15% against the release before last), which need no baseline file.
- Local runs (`Test-PreRelease.ps1`) pass `-DeterministicReportOnly`: only correctness fails.

To recalibrate after an intended work or allocation change, push the commit to a
`perf-calibration/<name>` branch (or dispatch *Performance Calibration*), then copy one replica's
`scale-smoke/cert-report.json`, `scale-standard/cert-report.json` and spill profile over
`baseline-smoke.json`, `baseline-standard.json` and
`spill-alloc-budgets/budget-10000000rows.json` in the same change. The commit message says why the
work or allocations changed and links the run. Check that the other replicas pass against the new
files before committing.

For HA capacity evidence, follow the workflow in [`../capacity-results/README.md`](../capacity-results/README.md). For release certification gates, use the pre-release and certification scripts referenced from [`../scripts/README.md`](../scripts/README.md).
