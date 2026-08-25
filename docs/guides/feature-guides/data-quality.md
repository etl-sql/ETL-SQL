# Validating Data Quality

Schema validation confirms the *structure* of incoming data, but value-level validation ensures that rows satisfy domain rules, ranges, formats, and relational constraints. ETL-SQL provides native, declarative data quality rules directly in SQL statements with automated quarantine routing and metrics capture.

For full statement syntax and options, see the [Data Quality Rules Reference](../../reference/statements/dml/data-quality-rules.md).

## The Data Quality Model

Data quality rules are declared as inline metadata tags (`/* @expect: '...'; @fail: '...'; */`) on columns or tables during `SELECT ... INTO` or insert pipelines.

```sql
SELECT
    user_id     /* @expect: 'NOT NULL'; @fail: 'THROW'; */,
    email       /* @expect: 'MATCHES ^[^@]+@[^@]+$'; @fail: 'QUARANTINE'; */,
    age         /* @expect: '>= 0, <= 120'; @fail: 'WARN'; */,
    status      /* @expect: "IN ('ACTIVE', 'PENDING', 'CLOSED')"; @fail: 'WARN'; */
INTO #clean_users
FROM raw_users
ON FAILURE QUARANTINE INTO #quarantined_users;
```

### Failure Actions

- **`THROW`**: Halts script execution immediately upon encountering any invalid row. Ideal for non-recoverable schema or primary key violations.
- **`WARN`**: Materializes rows normally while recording rule violation counts and sample values in diagnostic logs and `eng.data_quality_failures`.
- **`QUARANTINE`**: Diverts invalid rows into a dedicated quarantine target table while allowing valid rows to proceed downstream uninterrupted.

## Running Unattended Without Portal {#running-unattended-without-portal}

Data quality features run entirely in the engine context and require no external services:
1. **Local CLI**: Run `etl-sql run script.etlsql` to evaluate rules, quarantine invalid records, and exit with status codes based on threshold policies.
2. **SQLite History**: In unattended or Team deployments, Orchestrator stores run metrics, failure trends, and assertion histories locally.
3. **Workspace Policy**: Place an `etlsql-policy.json` file at the workspace root to enforce required tags, regex patterns, and threshold gates across developer machines and CI/CD pipelines.

## Modular Data Quality Guides

Explore focused guides for specific implementation patterns:

- [Column Quality Rules](../data-quality/column-quality-rules.md) — Syntax and patterns for null checks, regex matching, range checks, and type validations.
- [Quarantine & Remediation](../data-quality/quarantine-and-remediation.md) — Handling diverted rows, inspecting `__dq_*` capture columns, and executing `REPLAY QUARANTINE`.
- [Multi-Row & Cross-Table Rules](../data-quality/multi-row-and-cross-table-rules.md) — Deduplication with `UNIQUE_FIRST BY`, composite keys, and relational `EXISTS IN` validations.
- [Run-Level Assertions (`ASSERT JOB`)](../data-quality/run-level-assertions.md) — Validating batch volumes, row counts, anomaly thresholds, and notification triggers.
- [Automating Quality Gates](../data-quality/automating-quality-gates.md) — Integrating automated quality gates in GitHub Actions, scheduled cron, and enterprise CI.
- [Data Stewardship & Impact Analysis](../data-quality/data-stewardship-and-impact.md) — Column tagging, metadata coverage audits, and downstream impact analysis.

## References

- [Data Quality Rules Reference](../../reference/statements/dml/data-quality-rules.md)
- [ASSERT JOB Reference](../../reference/statements/session-control/assert-job.md)
- [Lineage Reference](../../reference/statements/session-control/lineage.md)
- [`eng.data_quality_status` Table](../../reference/eng/data-quality-status.md)
