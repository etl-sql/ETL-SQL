# etl-sql admin ha-soak fault-run

Run the bounded HA fault-injection harness

## Synopsis

```text
etl-sql admin ha-soak fault-run [options]
```

## Options

| Option | Description |
| :--- | :--- |
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
