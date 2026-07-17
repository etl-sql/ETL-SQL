# etl-sql admin ha-soak diagnostics

Export a redacted diagnostics bundle for a topology run

## Synopsis

```text
etl-sql admin ha-soak diagnostics [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--force, -f` | Overwrite existing generated HA soak artifacts. |
| `--log-tail` | Number of Docker log lines per service to include in diagnostics. |
| `--no-docker` | Skip Docker status/log capture when exporting diagnostics. |
| `--output-root` | Directory for generated HA soak runs or diagnostics. |
| `--run-root, -r` | Path to a generated HA soak topology run root. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
