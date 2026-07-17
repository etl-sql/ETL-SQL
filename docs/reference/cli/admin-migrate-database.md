# etl-sql admin migrate-database

Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment

## Synopsis

```text
etl-sql admin migrate-database [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--dry-run` | Verify counts and target schema compatibility without writing any data. |
| `--from` | Source database provider (only 'sqlite' is supported). |
| `--to` | Target database provider (only 'postgres' is supported). |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
