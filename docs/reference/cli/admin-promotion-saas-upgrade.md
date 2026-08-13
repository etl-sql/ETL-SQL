# etl-sql admin promotion saas-upgrade

Drain and upgrade one Managed Dedicated tenant boundary

## Synopsis

```text
etl-sql admin promotion saas-upgrade [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--execute` | Fence scheduling, drain durable admissions, and apply the authorized cutover. |
| `--max-concurrent-jobs` | Concurrent-job capacity assertion; must match signed upgrade authorization. |
| `--max-report-sessions` | Report-session capacity assertion; must match signed upgrade authorization. |
| `--max-storage-mb` | Storage-capacity assertion in MiB; must match signed upgrade authorization. |
| `--target-release` | Release or immutable image digest assertion; must match signed upgrade authorization. |
| `--tenant` | Tenant assertion; must match the active signed operation authorization. |
| `--tenant-root` | Provisioned Managed Dedicated tenant boundary to upgrade. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
