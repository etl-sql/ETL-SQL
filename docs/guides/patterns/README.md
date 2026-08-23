# Patterns, Best Practices & Troubleshooting

[« Back to Guides](../README.md)

This section contains operational playbooks, sample catalog maps, and domain-focused troubleshooting guides.

---

## Troubleshooting Guides

| Guide | Description |
| :--- | :--- |
| [Troubleshooting: Syntax & Dialects](troubleshooting-syntax-and-dialect.md) | Solutions for dialect mismatches (`TOP` vs `LIMIT`), function availability, and polling loops. |
| [Troubleshooting: Connections & Security](troubleshooting-connections-and-security.md) | Authentication conflicts, `ENC:` credential resolution, `CREATE SETS`, and safe zone limits. |
| [Troubleshooting: Report-SQL](troubleshooting-reporting.md) | Solutions for `RELDATE` casting errors, Tier 2 traps, cascading slicers, and action bindings. |
| [Troubleshooting: Performance](troubleshooting-performance.md) | Resolving slow cross-source joins, streaming massive files, and memory spill optimization. |
| [ETL-SQL FAQ](faq.md) | Central searchable Q&A covering core concepts, commands, and best practice links. |

---

## Operational Patterns & Samples

| Guide | Description |
| :--- | :--- |
| [Sample Guide](sample-guide.md) | Comprehensive map of 160+ runnable `.etlsql` and `.rptsql` samples in `/samples/`. |
| [One-Person Quality Loop](../operations/one-person-quality-loop.md) | Complete workstation runbook: workspace policy, CLI validation, local schedules, and operator reports. |

---

## Best Practices Links

- [Staged vs. Direct Streaming Ingestion](../pipelines/staged-vs-streaming-ingestion.md)
- [Script Resilience & Checkpoints](../pipelines/script-resilience-and-checkpoints.md)
- [Authoring Dashboards](../reporting/authoring-dashboards.md)
- [Configuring Script Logging](../operations/configuring-script-logging.md)
