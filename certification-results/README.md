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
| `baseline-*.json` / `baseline-*.md` | Reference certification baselines used for comparisons. |
| `*-scenarios.json` / `*-matrix.json` | Scenario definitions used by certification scripts. |
| `columnar-crossover-benchmarks/` | Columnar crossover benchmark evidence. |
| `ha-fault-injection/` | Fault-injection plans, cleanup invariants, and per-fault outcomes. |
| `ha-large-job-soak/` | Large-job HA soak plans and outcomes. |
| `postgres-ha-soak/` | PostgreSQL HA service-capacity reports and metrics snapshots. |
| `spill-alloc-*` | Spill allocation budgets and measured profiles. |

## Producing new evidence

Use the certification and capacity scripts under [`../scripts`](../scripts) and keep generated credentials outside this directory. When a run becomes a product baseline, add a short Markdown interpretation beside the JSON so reviewers can see what changed and why it matters.

For HA capacity evidence, follow the workflow in [`../capacity-results/README.md`](../capacity-results/README.md). For release certification gates, use the pre-release and certification scripts referenced from [`../scripts/README.md`](../scripts/README.md).
