# etl-sql admin ha-soak workload

Materialize the sustained-load workload config for a topology run

## Synopsis

```text
etl-sql admin ha-soak workload [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--admin-password` | Admin password to place in the local workload config; defaults to the generated run-root password. |
| `--force, -f` | Overwrite existing generated HA soak artifacts. |
| `--output, -o` | Destination file path for the generated HA soak artifact. |
| `--run-root, -r` | Path to a generated HA soak topology run root. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
