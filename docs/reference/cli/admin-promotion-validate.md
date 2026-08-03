# etl-sql admin promotion validate

Validate mappings and collisions without changing the target

## Synopsis

```text
etl-sql admin promotion validate [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--bind` | Target binding in SOURCE=TARGET form (repeatable). |
| `--output, -o` | Destination for the versioned JSON inventory (default: deployment-preflight.json). |
| `--package, -p` | Path to a versioned Orchestrator promotion package. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
