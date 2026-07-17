# Admin operations samples

These scripts are **examples** of operational automation written in ETL-SQL. As of v0.15.0 each has
a managed, first-class replacement in the Portal — three native background services
configured under `Portal:AdminServices` with HA-safe scheduling, delivery retries, run history,
and audit (see *Native admin services* in `docs/guides/administration.md`):

| Script | Native replacement |
| :--- | :--- |
| `daily_failure_digest.etlsql` | `Portal:AdminServices:FailureDigest` |
| `backup_and_report.etlsql` | `Portal:AdminServices:BackupReport` — `etl-sql admin backup` now records its outcome automatically, so the two-step scheduler wiring here is no longer needed |
| `capacity_report.etlsql` | `Portal:AdminServices:CapacityReport` |

Keep using these scripts as starting points for *custom* operational workflows; for the three
workflows above, the supported production path is the native configuration.
