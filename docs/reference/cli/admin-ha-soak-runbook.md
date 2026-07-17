# etl-sql admin ha-soak runbook

Generate an ordered operator runbook for a topology run

## Synopsis

```text
etl-sql admin ha-soak runbook [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--force, -f` | Overwrite existing generated HA soak artifacts. |
| `--mode` | Plan depth: CiSmoke or ManualCertification. |
| `--output, -o` | Destination file path for the generated HA soak artifact. |
| `--run-root, -r` | Path to a generated HA soak topology run root. |
| `--workload` | Path to the materialized sustained-workload JSON. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
