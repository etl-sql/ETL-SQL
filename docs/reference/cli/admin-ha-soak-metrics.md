# etl-sql admin ha-soak metrics

Capture a non-secret PostgreSQL metrics snapshot

## Synopsis

```text
etl-sql admin ha-soak metrics [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--force, -f` | Overwrite existing generated HA soak artifacts. |
| `--output, -o` | Destination file path for the generated HA soak artifact. |
| `--run-root, -r` | Path to a generated HA soak topology run root. |
| `--validate-only` | Validate the topology/script contract without writing runtime artifacts. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
