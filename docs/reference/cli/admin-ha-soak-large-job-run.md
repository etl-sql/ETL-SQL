# etl-sql admin ha-soak large-job-run

Run the bounded concurrent large-job soak harness

## Synopsis

```text
etl-sql admin ha-soak large-job-run [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--duration-seconds` | Override runner duration in seconds for bounded local execution. |
| `--force, -f` | Overwrite existing generated HA soak artifacts. |
| `--output-root` | Directory for generated HA soak runs or diagnostics. |
| `--plan` | Existing HA soak plan path to execute. |
| `--run-root, -r` | Path to a generated HA soak topology run root. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
