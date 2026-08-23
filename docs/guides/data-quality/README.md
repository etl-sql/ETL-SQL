# Data Quality & Governance Guides

[« Back to Guides](../README.md)

ETL-SQL provides deep value-level validation, anomaly detection, quarantine routing, automated remediation, and lineage stewardship. These guides explain how to build robust, self-healing pipelines.

---

## Guides in this Section

| Guide | Description |
| :--- | :--- |
| [Column Quality Rules](column-quality-rules.md) | Declare inline `@expect` / `@fail` rules for null checks, regex patterns, range boundaries, and castability. |
| [Quarantine & Remediation](quarantine-and-remediation.md) | Divert invalid rows to durable capture tables, inspect `__dq_*` metadata, and reprocess with `REPLAY QUARANTINE`. |
| [Multi-Row & Cross-Table Rules](multi-row-and-cross-table-rules.md) | Enforce `UNIQUE_FIRST BY`, composite uniqueness, cross-table `EXISTS IN`, and scoped multi-tenant boundaries. |
| [Run-Level Assertions (`ASSERT JOB`)](run-level-assertions.md) | Evaluate load volume anomalies against historical baselines, freshness thresholds, and notification transitions. |
| [Automating Quality Gates](automating-quality-gates.md) | Run quality checks in GitHub Actions, Task Scheduler, and Cron with `etlsql-policy.json` enforcement. |
| [Data Stewardship & Impact Analysis](data-stewardship-and-impact.md) | Script-first tags, metadata gap detection, protected data audits, and lineage impact queries. |

---

## Related References

- [Data Quality Rules Reference](../../reference/statements/dml/data-quality-rules.md)
- [ASSERT JOB Reference](../../reference/statements/session-control/assert-job.md)
- [Lineage Reference](../../reference/statements/session-control/lineage.md)
