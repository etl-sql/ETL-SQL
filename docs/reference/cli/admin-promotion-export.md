# etl-sql admin promotion export

Export eligible Orchestrator catalog and governance state

## Synopsis

```text
etl-sql admin promotion export [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--history-limit` | Maximum quality-history and lineage records to export (default: 10000). |
| `--output, -o` | Destination for the versioned JSON inventory (default: deployment-preflight.json). |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
