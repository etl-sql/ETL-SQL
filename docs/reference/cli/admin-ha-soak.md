# etl-sql admin ha-soak

Prepare and collect PostgreSQL HA soak certification artifacts

## Synopsis

```text
etl-sql admin ha-soak <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`diagnostics`](admin-ha-soak-diagnostics.md) | Export a redacted diagnostics bundle for a topology run |
| [`evidence`](admin-ha-soak-evidence.md) | Generate the non-secret HA soak evidence checklist |
| [`fault-plan`](admin-ha-soak-fault-plan.md) | Generate the HA fault-injection plan |
| [`fault-run`](admin-ha-soak-fault-run.md) | Run the bounded HA fault-injection harness |
| [`large-job-plan`](admin-ha-soak-large-job-plan.md) | Generate the concurrent large-job soak plan |
| [`large-job-run`](admin-ha-soak-large-job-run.md) | Run the bounded concurrent large-job soak harness |
| [`metrics`](admin-ha-soak-metrics.md) | Capture a non-secret PostgreSQL metrics snapshot |
| [`prepare`](admin-ha-soak-prepare.md) | Generate an isolated PostgreSQL HA soak topology run root |
| [`runbook`](admin-ha-soak-runbook.md) | Generate an ordered operator runbook for a topology run |
| [`validate`](admin-ha-soak-validate.md) | Validate completed HA soak evidence before citing it |
| [`workload`](admin-ha-soak-workload.md) | Materialize the sustained-load workload config for a topology run |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
