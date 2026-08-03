# etl-sql admin promotion preflight

Create a secret-safe, mutation-free promotion inventory

## Synopsis

```text
etl-sql admin promotion preflight [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--from-profile` | Source deployment profile: Solo, Team, Enterprise, or SaaS. |
| `--output, -o` | Destination for the versioned JSON inventory (default: deployment-preflight.json). |
| `--source, -s` | Workspace or export root to inventory (default: current directory). |
| `--to-profile` | Target deployment profile: Solo, Team, Enterprise, or SaaS. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
